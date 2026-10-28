using JinYong.Core.Session;

namespace JinYong.Core.Maps;

public sealed record WorldTravelSnapshot(GridPosition Position, Direction Facing, bool InShip,
    GridPosition ShipPosition, GridPosition ShipBow, Direction ShipFacing);
public enum WorldTravelBlock { None, Boundary, Building, Terrain, Water, Surface }
public sealed record WorldTravelResult(WorldTravelSnapshot State, GridPosition Target, WorldTravelBlock Block,
    int? EntranceSceneId = null, bool Boarded = false, bool Disembarked = false)
{
    public bool CanMove => Block == WorldTravelBlock.None && EntranceSceneId is null;
}
public sealed record SceneTravelTarget(int? SceneId, GridPosition? Position, bool ReturnWorld);

/// <summary>
/// Pure movement/entrance rules from OpenLegend src/world/world_map.cpp and
/// scene.cpp resolve_scene_transition. Inventory and event execution stay in the
/// existing game session. World recovery/weather/animation clocks are not owned here.
/// </summary>
public static class WorldTravelRules
{
    private static readonly (int First, int Last)[] BlockedWalking =
    [(358,362),(374,380),(458,464),(506,670),(818,824),(838,838),(934,936),(1016,1022)];
    private static readonly (int First, int Last)[] Land =
    [(4,356),(364,372),(382,456),(672,954),(466,504),(1000,1014)];
    private static readonly (int First, int Last)[] ShipCoast =
    [(358,362),(374,380),(458,464),(506,610),(1016,1022)];
    private static bool InRange(int value, (int First, int Last)[] ranges) => ranges.Any(range => value >= range.First && value <= range.Last);
    private static bool Clear(WorldMapDefinition map, GridPosition point) => map.AtOrZero(WorldLayer.Building, point) == 0 &&
        map.AtOrZero(WorldLayer.BuildX, point) == 0 && map.AtOrZero(WorldLayer.BuildY, point) == 0;

    public static WorldTravelResult Probe(WorldMapDefinition map, WorldTravelSnapshot state, Direction direction,
        SceneWorldState? scenes = null, PartyState? party = null, bool detectEntrances = true)
    {
        Validate(state);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        var step = state.Position.Step(direction);
        var target = new GridPosition(Math.Clamp(step.X, 0, WorldMapDefinition.Extent - 1), Math.Clamp(step.Y, 0, WorldMapDefinition.Extent - 1));
        var facing = state.InShip ? state with { ShipFacing = direction } : state with { Facing = direction };
        if (detectEntrances && scenes is not null && FindEntrance(scenes, party, target) is { } entrance)
            return new(facing, target, WorldTravelBlock.None, entrance);
        var coordinate = direction is Direction.Left or Direction.Right ? target.X : target.Y;
        var movedAxisInside = coordinate is > 10 and < 459;
        var delta = new GridPosition(step.X - state.Position.X, step.Y - state.Position.Y);
        WorldTravelResult Blocked(WorldTravelBlock block) => new(state.InShip ? facing with
        {
            ShipPosition = state.Position, ShipBow = step
        } : facing, target, block);
        if (target == state.Position) return Blocked(WorldTravelBlock.Boundary);
        if (!state.InShip)
        {
            // Either occupied ship cell can be boarded; the DOS path checks this
            // before normal land collision and boundary guards.
            var boarding = target == state.ShipPosition || target == state.ShipBow;
            if (boarding)
            {
                var bow = direction is Direction.Left or Direction.Right ?
                    new GridPosition(target.X + delta.X, state.ShipBow.Y) : new GridPosition(state.ShipBow.X, target.Y + delta.Y);
                return new(facing with { Position = target, InShip = true, ShipPosition = target, ShipBow = bow, ShipFacing = direction },
                    target, WorldTravelBlock.None, Boarded: true);
            }
            if (!movedAxisInside) return Blocked(WorldTravelBlock.Boundary);
            if (!Clear(map, target)) return Blocked(WorldTravelBlock.Building);
            if (InRange(map.At(WorldLayer.Earth, target), BlockedWalking)) return Blocked(WorldTravelBlock.Terrain);
            // Surface is intentionally ignored by walking, exactly as in the DOS rule.
            return new(facing with { Position = target }, target, WorldTravelBlock.None);
        }
        if (!movedAxisInside) return Blocked(WorldTravelBlock.Boundary);
        var next = new GridPosition(target.X + delta.X, target.Y + delta.Y);
        var buildingClear = map.AtOrZero(WorldLayer.Building, target) == 0 || map.AtOrZero(WorldLayer.Building, next) == 0;
        var ownerClear = map.AtOrZero(WorldLayer.BuildX, target) == 0 && map.AtOrZero(WorldLayer.BuildY, target) == 0 ||
            map.AtOrZero(WorldLayer.BuildX, next) == 0 && map.AtOrZero(WorldLayer.BuildY, next) == 0;
        var water = InRange(map.At(WorldLayer.Earth, target), ShipCoast) || InRange(map.AtOrZero(WorldLayer.Earth, next), ShipCoast);
        if (buildingClear && ownerClear && water)
            return new(facing with { Position = target, ShipPosition = target, ShipBow = next }, target, WorldTravelBlock.None);
        if (Clear(map, target) && InRange(map.At(WorldLayer.Earth, target), Land))
        {
            if (map.At(WorldLayer.Surface, target) != 0) return Blocked(WorldTravelBlock.Surface);
            // Disembarkation leaves both ship cells where they were before landing.
            return new(facing with { Position = target, InShip = false, Facing = direction }, target, WorldTravelBlock.None, Disembarked: true);
        }
        return Blocked(!buildingClear || !ownerClear ? WorldTravelBlock.Building : WorldTravelBlock.Water);
    }

    public static int? FindEntrance(SceneWorldState scenes, PartyState? party, GridPosition target)
    {
        foreach (var id in scenes.SceneIds.Where(id => id is >= 0 and < 84).Order())
        {
            if (!scenes.HasLegacyMetadata(id)) continue;
            var first = new GridPosition(scenes.MetadataWord(id, 10), scenes.MetadataWord(id, 11));
            var second = new GridPosition(scenes.MetadataWord(id, 12), scenes.MetadataWord(id, 13));
            if (target != first && target != second) continue;
            var condition = scenes.MetadataWord(id, 9);
            if (condition == 0 || condition == 2 && party is not null &&
                party.Members.Any(role => role >= 0 && party.GetWord(role, RoleWords.Speed) >= 70)) return id;
        }
        return null;
    }

    public static GridPosition SceneEntry(SceneWorldState scenes, int sceneId, bool useJumpReturn = false)
    {
        if (!scenes.HasLegacyMetadata(sceneId)) throw new InvalidOperationException($"Scene {sceneId} has no travel metadata.");
        return new(Math.Clamp(scenes.MetadataWord(sceneId, useJumpReturn ? 24 : 14), 0, 63),
            Math.Clamp(scenes.MetadataWord(sceneId, useJumpReturn ? 25 : 15), 0, 63));
    }

    public static SceneTravelTarget? ResolveSceneTransition(SceneWorldState scenes, int sceneId, GridPosition position)
    {
        if (!scenes.HasLegacyMetadata(sceneId)) return null;
        for (var exit = 0; exit < 3; exit++)
            if (position == new GridPosition(scenes.MetadataWord(sceneId, 16 + exit), scenes.MetadataWord(sceneId, 19 + exit)))
                return new(null, null, true);
        var jump = scenes.MetadataWord(sceneId, 8);
        if (jump < 0 || position != new GridPosition(scenes.MetadataWord(sceneId, 22), scenes.MetadataWord(sceneId, 23))) return null;
        if (jump >= 84 || !scenes.HasLegacyMetadata(jump)) throw new InvalidOperationException($"Scene jump target {jump} has no original metadata.");
        var useReturn = scenes.MetadataWord(sceneId, 10) == 0 && scenes.MetadataWord(sceneId, 11) == 0;
        return new(jump, SceneEntry(scenes, jump, useReturn), false);
    }

    internal static void Validate(WorldTravelSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!WorldMapDefinition.Contains(state.Position)) throw new ArgumentOutOfRangeException(nameof(state), "World position is outside the map.");
        if (!Enum.IsDefined(state.Facing) || !Enum.IsDefined(state.ShipFacing)) throw new ArgumentException("Invalid travel direction.", nameof(state));
        // ShipBow may be a lookahead coordinate, so preserve it even off-map.
    }
}

public sealed class WorldTravelState
{
    public WorldTravelState(WorldTravelSnapshot snapshot) => Restore(snapshot);
    public WorldTravelSnapshot Snapshot { get; private set; } = null!;
    public void Restore(WorldTravelSnapshot snapshot) { WorldTravelRules.Validate(snapshot); Snapshot = snapshot; }
    public WorldTravelResult TryMove(WorldMapDefinition map, Direction direction, SceneWorldState? scenes = null,
        PartyState? party = null, bool detectEntrances = true)
    {
        var result = WorldTravelRules.Probe(map, Snapshot, direction, scenes, party, detectEntrances);
        Snapshot = result.State;
        return result;
    }
}
