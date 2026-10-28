using Godot;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class SceneView
{
    private void DrawWelcome()
    {
        for (var y = 20; y < Size.Y; y += 32)
        for (var x = 20; x < Size.X; x += 32)
            DrawCircle(new Vector2(x, y), 0.65f, new Color("263c35"));
        var text = "江湖待启";
        var width = _font.GetStringSize(text, fontSize: 34).X;
        DrawString(_font, Size / 2 - new Vector2(width / 2, 12), text, fontSize: 34, modulate: new Color("c9b992"));
        text = "选择原版游戏目录，进入熟悉的江湖";
        width = _font.GetStringSize(text, fontSize: 15).X;
        DrawString(_font, Size / 2 + new Vector2(-width / 2, 26), text, fontSize: 15, modulate: new Color("83978b"));
    }

    private void DrawExploration()
    {
        if (_session is null) return;
        var scene = _session.Scene;
        var player = _session.Player;
        Vector2 Project(GridPosition point, int height = 0)
        {
            var (x, y) = IsometricProjection.Project(point, height);
            return new Vector2(x, y);
        }
        var motion = _session.ScriptedMotion;
        var fromCell = motion?.From ?? _walker!.From;
        var toCell = motion?.To ?? _walker!.To;
        if (motion is null && !_walker!.IsMoving) fromCell = toCell = player.Position;
        var progress = SmoothMovement ? motion?.Progress ?? _walker!.Progress : 1;
        var from = Project(fromCell, scene.At(MapLayer.BuildingHeight, fromCell));
        var to = Project(toCell, scene.At(MapLayer.BuildingHeight, toCell));
        var actor = from.Lerp(to, (float)progress);
        var camera = _session.Camera is { } view
            ? new Vector2((float)((view.X - view.Y) * 18), (float)((view.X + view.Y) * 9)) : actor;
        var origin = Size / 2 - camera * _zoom;
        DrawnSprites = 0;
        void Sprite(int id, Vector2 anchor)
        {
            if (!_frames.TryGetValue(id, out var frame)) return;
            var rectangle = new Rect2(origin + (anchor - frame.Offset) * _zoom, frame.Region.Size * _zoom);
            if (!rectangle.Intersects(new Rect2(Vector2.Zero, Size))) return;
            DrawTextureRectRegion(frame.Texture, rectangle, frame.Region);
            DrawnSprites++;
        }

        // Preserve OpenLegend's x-major two-pass draw order, offsets and height
        // layers. Sorting everything solely by screen Y breaks tall structures.
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
        {
            var point = new GridPosition(x, y);
            if (scene.At(MapLayer.BuildingHeight, point) == 0)
                Sprite(scene.At(MapLayer.Ground, point), Project(point));
        }
        var actorCell = progress < 0.5 ? fromCell : toCell;
        var firstFrame = new[] { 5002, 5016, 5030, 5044 }[(int)player.Facing];
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
        {
            var point = new GridPosition(x, y);
            var anchor = Project(point);
            var height = scene.At(MapLayer.BuildingHeight, point);
            if (height != 0) Sprite(scene.At(MapLayer.Ground, point), anchor);
            var building = scene.At(MapLayer.Building, point);
            if (building is not 0 and not 15000) Sprite(building, anchor - new Vector2(0, height));
            if (scene.EventAt(point) is { Picture: > 0 } pointEvent)
                Sprite(pointEvent.Picture, anchor - new Vector2(0, height));
            if (point == actorCell) Sprite(player.PictureOverride ?? (firstFrame + player.WalkFrameOffset), actor);
            var decoration = scene.At(MapLayer.Decoration, point);
            if (decoration != 0) Sprite(decoration, anchor - new Vector2(0, scene.At(MapLayer.DecorationHeight, point)));
        }
        if (ShowCollision)
        {
            for (var x = Math.Max(0, player.Position.X - 14); x <= Math.Min(63, player.Position.X + 14); x++)
            for (var y = Math.Max(0, player.Position.Y - 14); y <= Math.Min(63, player.Position.Y + 14); y++)
            {
                var point = new GridPosition(x, y);
                var center = origin + Project(point, scene.At(MapLayer.BuildingHeight, point)) * _zoom;
                var block = SceneMovementRules.Check(scene, point, point);
                var color = block == MovementBlock.None ? new Color(0.5f, 0.8f, 0.55f, 0.25f) : new Color(1, 0.4f, 0.25f, 0.7f);
                DrawPolyline([center + new Vector2(-18, 0) * _zoom, center + new Vector2(0, -9) * _zoom,
                    center + new Vector2(18, 0) * _zoom, center + new Vector2(0, 9) * _zoom,
                    center + new Vector2(-18, 0) * _zoom], color);
            }
        }
        if (_session.FadeOpacity > 0)
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, (float)_session.FadeOpacity));
    }
}
