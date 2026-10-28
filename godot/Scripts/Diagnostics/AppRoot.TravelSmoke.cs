using Godot;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class AppRoot
{
    private bool _travelSmoke;
    private int _travelChecks, _travelSmokeStage, _travelPathIndex;
    private List<Direction> _travelPath = [];
    private GameSession? _travelSmokeSession;
    private PartyState? _travelSmokeParty;
    private EventRunner? _travelSmokeEvents;
    private SceneRuntimeState? _travelSmokeHome;
    private Dictionary<int, int> _travelInventory = [];
    private short[][] _travelEventWords = [];
    private short[] _travelMapWords = [];
    private int _travelOpeningCount;
    private bool _travelWorldCaptured, _capturingTravelWorld;

    private void AdvanceTravelSmoke()
    {
        var session = _world.Session!;
        if (_travelSmokeSession is not null && !ReferenceEquals(session, _travelSmokeSession))
        { _ = FinishSmokeAsync(false, "Travel replaced the authoritative game session."); return; }
        // Always release an acknowledged direction before waiting for fades or
        // dialogue; SceneWalker must observe a real released input frame.
        if (_smokeAction is not null)
        {
            if (!_smokeAcknowledged) return;
            Input.ActionRelease(_smokeAction); _smokeAction = null; _travelPathIndex++;
            return;
        }
        if (session.IsEventRunning)
        {
            if (session.Events?.Pending is { } pending)
            {
                if (pending.Kind == EventPresentationKind.Choice)
                { _ = FinishSmokeAsync(false, "Travel route unexpectedly encountered a story choice."); return; }
                _smokePages.Add(pending.Token);
                Input.ActionPress("interact"); _smokeConfirmPressed = true;
            }
            return;
        }
        if (session.IsInputLocked || _world.IsMoving) return;
        if (_travelSmokeStage == 0)
        {
            if (session.IsWorld || session.World.CurrentSceneId != 70 || session.MainMap is null || session.Travel is null)
            { _ = FinishSmokeAsync(false, "Travel smoke requires the full original library, starting inside scene 70."); return; }
            _travelOpeningCount = session.Events?.CompletedPrograms.Count(id => id == 691) ?? 0;
            if (_travelOpeningCount != 1 || session.Scene.Events[0][4] != -1)
            { _ = FinishSmokeAsync(false, "Home opening state was not applied exactly once before travel."); return; }
            _travelSmokeSession = session; _travelSmokeParty = session.Party; _travelSmokeEvents = session.Events;
            _travelSmokeHome = session.Scene; _travelInventory = session.Inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
            _travelEventWords = session.Scene.Events.Select(point => Enumerable.Range(0, 11).Select(field => point[field]).ToArray()).ToArray();
            _travelMapWords = CaptureTravelMap(session.Scene);
            var exits = Enumerable.Range(0, 3).Select(index => new GridPosition(session.World.MetadataWord(70, 16 + index),
                session.World.MetadataWord(70, 19 + index))).Where(SceneDefinition.Contains).ToHashSet();
            var path = HomeExitRoute(session.Scene, session.Player.Position, exits);
            if (path is null || path.Count == 0)
            { _ = FinishSmokeAsync(false, "No safe keyboard path from the home spawn to an original exit."); return; }
            _travelPath = path; _travelPathIndex = 0; _travelSmokeStage = 1; _travelChecks++;
        }
        if (_travelPathIndex < _travelPath.Count)
        {
            _smokeAcknowledged = false;
            _smokeAction = ActionName(_travelPath[_travelPathIndex]);
            Input.ActionPress(_smokeAction);
            return;
        }
        if (_travelSmokeStage == 1)
        {
            if (!session.IsWorld)
            { _ = FinishSmokeAsync(false, "Walking to the home exit did not activate the world view."); return; }
            if (_capturePath is not null && !_travelWorldCaptured && DisplayServer.GetName() != "headless")
            {
                if (!_capturingTravelWorld) _ = CaptureTravelWorldAsync();
                return;
            }
            if (!ReferenceEquals(_travelSmokeHome, session.Scene) || !ReferenceEquals(_travelSmokeParty, session.Party) ||
                !ReferenceEquals(_travelSmokeEvents, session.Events))
            { _ = FinishSmokeAsync(false, "Entering the world discarded home state or the event runtime."); return; }
            var path = WorldReturnRoute(session);
            if (path is null)
            { _ = FinishSmokeAsync(false, "No safe world excursion and keyboard return path to home was found."); return; }
            _travelPath = path; _travelPathIndex = 0; _travelSmokeStage = 2; _travelChecks += 2;
            return;
        }
        if (session.IsWorld || session.World.CurrentSceneId != 70)
        { _ = FinishSmokeAsync(false, "The world entrance did not return to the original home scene."); return; }
        var inventorySame = _travelInventory.OrderBy(pair => pair.Key).SequenceEqual(session.Inventory.OrderBy(pair => pair.Key));
        var eventsSame = Enumerable.Range(0, 200).All(index => Enumerable.Range(0, 11)
            .All(field => _travelEventWords[index][field] == session.Scene.Events[index][field]));
        var mapSame = _travelMapWords.SequenceEqual(CaptureTravelMap(session.Scene));
        var openingSame = session.Events?.CompletedPrograms.Count(id => id == 691) == _travelOpeningCount && session.Scene.Events[0][4] == -1;
        var identitySame = ReferenceEquals(_travelSmokeSession, session) && ReferenceEquals(_travelSmokeHome, session.Scene) &&
            ReferenceEquals(_travelSmokeEvents, session.Events) && ReferenceEquals(_travelSmokeParty, session.Party);
        if (inventorySame) _travelChecks++;
        if (eventsSame && mapSame) _travelChecks++;
        if (openingSame && identitySame) _travelChecks++;
        var success = inventorySame && eventsSame && mapSame && openingSame && identitySame && _world.DrawnSprites > 0;
        _ = FinishSmokeAsync(success, success
            ? "Keyboard travel completed: home exit, world excursion, home entrance; checked session, inventory, opening, events and map persistence."
            : $"Travel persistence failed: inventory={inventorySame}, events={eventsSame}, map={mapSame}, opening={openingSame}, identities={identitySame}.");
    }

    private async Task CaptureTravelWorldAsync()
    {
        _capturingTravelWorld = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_capturePath!))!,
            Path.GetFileNameWithoutExtension(_capturePath) + "-world.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { await FinishSmokeAsync(false, "World screenshot failed."); return; }
        _travelWorldCaptured = true;
        _capturingTravelWorld = false;
    }

    private static short[] CaptureTravelMap(ISceneMap map) => Enumerable.Range(0, SceneDefinition.LayerCount * SceneDefinition.TileCount)
        .Select(index => map.At((MapLayer)(index / SceneDefinition.TileCount),
            new((index % SceneDefinition.TileCount) % 64, (index % SceneDefinition.TileCount) / 64))).ToArray();

    private static List<Direction>? HomeExitRoute(ISceneMap scene, GridPosition start, HashSet<GridPosition> exits)
    {
        var paths = new Dictionary<GridPosition, List<Direction>> { [start] = [] };
        var queue = new Queue<GridPosition>([start]);
        while (queue.TryDequeue(out var current))
        foreach (var direction in Enum.GetValues<Direction>())
        {
            var next = current.Step(direction);
            if (paths.ContainsKey(next) || SceneMovementRules.Check(scene, current, next) != MovementBlock.None) continue;
            // Do not route through an automatic plot, even when its tile is walkable.
            if (scene.EventAt(next) is { } point && point[4] > 0) continue;
            var path = new List<Direction>(paths[current]) { direction };
            if (exits.Contains(next)) return path;
            paths.Add(next, path); queue.Enqueue(next);
        }
        return null;
    }

    private static List<Direction>? WorldReturnRoute(GameSession session)
    {
        var map = session.MainMap!; var start = session.Travel!.Snapshot;
        List<Direction>? excursion = null;
        foreach (var direction in Enum.GetValues<Direction>())
        {
            var away = WorldTravelRules.Probe(map, start, direction, session.World, session.Party);
            if (!away.CanMove || away.Boarded || away.Disembarked || away.State.Position == start.Position) continue;
            var backDirection = direction switch { Direction.Up => Direction.Down, Direction.Down => Direction.Up,
                Direction.Left => Direction.Right, _ => Direction.Left };
            var back = WorldTravelRules.Probe(map, away.State, backDirection, session.World, session.Party);
            if (back.CanMove && !back.Boarded && !back.Disembarked && back.State.Position == start.Position)
            { excursion = [direction, backDirection]; break; }
        }
        if (excursion is null) return null;
        var queue = new Queue<WorldTravelSnapshot>([start]);
        var paths = new Dictionary<GridPosition, List<Direction>> { [start.Position] = [] };
        while (queue.TryDequeue(out var current) && paths.Count <= 2048)
        foreach (var direction in Enum.GetValues<Direction>())
        {
            var move = WorldTravelRules.Probe(map, current, direction, session.World, session.Party);
            if (move.EntranceSceneId == 70) return [.. excursion, .. paths[current.Position], direction];
            if (!move.CanMove || move.Boarded || move.Disembarked || paths.ContainsKey(move.State.Position)) continue;
            var path = new List<Direction>(paths[current.Position]) { direction };
            paths.Add(move.State.Position, path); queue.Enqueue(move.State);
        }
        return null;
    }
}
