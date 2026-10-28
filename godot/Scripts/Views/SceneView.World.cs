using Godot;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class SceneView
{
    private enum WorldDepthKind { Building, Player, Ship }
    private sealed record WorldDepthEntry(GridPosition Owner, WorldDepthKind Kind, int Picture = 0);

    /// <summary>
    /// Draws the active main map using the world bank already installed in _frames.
    /// The caller owns the background/fade. This method never changes travel state.
    /// Reference: OpenLegend world/world_map.cpp and render/world_depth_order.cpp.
    /// </summary>
    private void DrawWorld()
    {
        if (_session is not { MainMap: { } map, Travel: { } travel }) return;
        var state = travel.Snapshot;
        var player = _session.Player;
        var from = _walker?.From ?? state.Position;
        var to = _walker?.To ?? state.Position;
        var progress = SmoothMovement ? _walker?.Progress ?? 1 : 1;
        static Vector2 Project(GridPosition position) => new((position.X - position.Y) * 18, (position.X + position.Y) * 9);
        var actor = Project(from).Lerp(Project(to), (float)progress);
        var actorCell = progress < 0.5 ? from : to;
        var origin = Size / 2 - actor * _zoom;
        var viewport = new Rect2(Vector2.Zero, Size);
        DrawnSprites = 0;

        void Sprite(int picture, Vector2 anchor)
        {
            if (!_frames.TryGetValue(picture, out var frame)) return;
            var rectangle = new Rect2(origin + (anchor - frame.Offset) * _zoom, frame.Region.Size * _zoom);
            if (!rectangle.Intersects(viewport)) return;
            DrawTextureRectRegion(frame.Texture, rectangle, frame.Region);
            DrawnSprites++;
        }

        // Invert the projection for all padded viewport corners. Padding comes
        // from actual frame extents, so tall/offset buildings are not culled by
        // their anchor leaving the screen. Work scales with the visible region.
        var padding = Vector2.Zero;
        foreach (var frame in _frames.Values)
        {
            padding.X = Math.Max(padding.X, Math.Max(Math.Abs(frame.Offset.X), Math.Abs(frame.Region.Size.X - frame.Offset.X)));
            padding.Y = Math.Max(padding.Y, Math.Max(Math.Abs(frame.Offset.Y), Math.Abs(frame.Region.Size.Y - frame.Offset.Y)));
        }
        padding *= _zoom;
        Vector2 Unproject(Vector2 screen)
        {
            var point = (screen - origin) / _zoom;
            return new Vector2((point.X / 18 + point.Y / 9) / 2, (point.Y / 9 - point.X / 18) / 2);
        }
        var corners = new[]
        {
            Unproject(-padding), Unproject(new(Size.X + padding.X, -padding.Y)),
            Unproject(Size + padding), Unproject(new(-padding.X, Size.Y + padding.Y))
        };
        var beginX = Math.Clamp((int)Math.Floor(corners.Min(point => point.X)) - 1, 0, WorldMapDefinition.Extent);
        var beginY = Math.Clamp((int)Math.Floor(corners.Min(point => point.Y)) - 1, 0, WorldMapDefinition.Extent);
        var endX = Math.Clamp((int)Math.Ceiling(corners.Max(point => point.X)) + 1, 0, WorldMapDefinition.Extent);
        var endY = Math.Clamp((int)Math.Ceiling(corners.Max(point => point.Y)) + 1, 0, WorldMapDefinition.Extent);

        // Earth and surface are separate x-major passes, not per-cell pairs.
        // A covered palette index zero remains opaque in the shared atlas.
        for (var x = beginX; x < endX; x++)
        for (var y = beginY; y < endY; y++)
        {
            var cell = new GridPosition(x, y);
            Sprite(map.At(WorldLayer.Earth, cell), Project(cell));
        }
        for (var x = beginX; x < endX; x++)
        for (var y = beginY; y < endY; y++)
        {
            var cell = new GridPosition(x, y);
            var picture = map.At(WorldLayer.Surface, cell);
            if (picture != 0) Sprite(picture, Project(cell));
        }

        var depth = BuildWorldDepth(map, actorCell, state.ShipPosition, !state.InShip, beginX, beginY, endX, endY);
        var playerPicture = state.InShip ? _session.ShipPicture : player.PictureOverride ??
            new[] { 5002, 5016, 5030, 5044 }[(int)player.Facing] + player.WalkFrameOffset;
        foreach (var entry in depth)
        {
            switch (entry.Kind)
            {
                case WorldDepthKind.Player: Sprite(playerPicture, actor); break;
                case WorldDepthKind.Ship: Sprite(_session.ShipPicture, Project(state.ShipPosition)); break;
                case WorldDepthKind.Building:
                    if (entry.Picture != 0) Sprite(entry.Picture, Project(entry.Owner));
                    break;
            }
        }
    }

    private static List<WorldDepthEntry> BuildWorldDepth(WorldMapDefinition map, GridPosition player,
        GridPosition ship, bool parkedShip, int beginX, int beginY, int endX, int endY)
    {
        var entries = new List<WorldDepthEntry>();
        GridPosition Owner(GridPosition cell)
        {
            // The legacy depth algorithm injects actor ownership into temporary
            // grids. Resolve it here without copying or mutating the map layers.
            if (cell == player || parkedShip && cell == ship) return cell;
            return new(map.At(WorldLayer.BuildX, cell), map.At(WorldLayer.BuildY, cell));
        }

        // Keep the original owner-footprint scan and reorder, including moving
        // an already encountered building relative to later depth entries.
        // Actors use an explicit kind: a real BUILDING picture 5000 is not a
        // synthetic player marker. Smooth presentation switches actor cells at
        // half progress, matching the scene view's modern interpolation policy.
        for (var x = beginX; x < endX; x++)
        {
            var scanFloorY = beginY;
            for (var y = beginY; y < endY; y++)
            {
                var cell = new GridPosition(x, y);
                if (cell == player)
                {
                    entries.Add(new(cell, WorldDepthKind.Player));
                    continue;
                }
                if (parkedShip && cell == ship)
                {
                    entries.Add(new(cell, WorldDepthKind.Ship));
                    continue;
                }
                var owner = Owner(cell);
                if (owner == new GridPosition(0, 0) || !WorldMapDefinition.Contains(owner)) continue;
                var found = entries.FindIndex(entry => entry.Owner == owner);
                if (found < 0)
                {
                    entries.Add(new(owner, WorldDepthKind.Building, map.At(WorldLayer.Building, owner)));
                    continue;
                }
                if (found == entries.Count - 1) continue;
                for (var scanY = y - 1; scanY >= scanFloorY; scanY--)
                {
                    var above = Owner(new(x, scanY));
                    if (above == new GridPosition(0, 0)) continue;
                    if (above != owner && entries[found].Owner != above)
                    {
                        var last = entries[^1];
                        entries.RemoveAt(entries.Count - 1);
                        entries.Insert(found, last);
                    }
                }
                scanFloorY = y + 1;
            }
        }
        return entries;
    }
}
