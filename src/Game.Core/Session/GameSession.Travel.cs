using JinYong.Core.Maps;

namespace JinYong.Core.Session;

public sealed record TravelRequest(long Token, int? SceneId, GridPosition? Position, bool IsWorld);

public sealed partial class GameSession
{
    public static readonly TimeSpan TravelFadeTime = TimeSpan.FromMilliseconds(450);
    private TimeSpan _travelElapsed;
    private bool _fadingIn, _resolveAfterEvent;
    private double _travelFadeFrom;
    private long _travelToken;
    private Direction? _lastWorldEntryDirection;
    private int _shipFrameOffset;
    public WorldMapDefinition? MainMap { get; }
    public WorldTravelState? Travel { get; }
    public bool IsWorld { get; private set; }
    public TravelRequest? PendingTravel { get; private set; }
    public bool IsTravelReady => PendingTravel is not null && _travelElapsed >= TravelFadeTime;
    public bool IsInputLocked => IsEventRunning || Battle is not null || PendingTravel is not null || _fadingIn;
    public int ShipPicture => new[] { 7430,7438,7446,7454 }[(int)(Travel?.Snapshot.ShipFacing ?? Direction.Up)] + _shipFrameOffset;
    private int WorldPlayerPicture => new[] { 5002,5016,5030,5044 }[(int)Travel!.Snapshot.Facing] + _walkFrameOffset;

    /// <summary>Single session clock for event continuations and travel presentation.</summary>
    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (PendingTravel is not null)
        {
            _travelElapsed = elapsed >= TravelFadeTime - _travelElapsed ? TravelFadeTime : _travelElapsed + elapsed;
            FadeOpacity = _travelFadeFrom + (1 - _travelFadeFrom) * (_travelElapsed / TravelFadeTime);
            PresentationRevision++;
            return;
        }
        if (_fadingIn)
        {
            _travelElapsed = elapsed >= TravelFadeTime - _travelElapsed ? TravelFadeTime : _travelElapsed + elapsed;
            FadeOpacity = _travelFadeFrom * (1 - _travelElapsed / TravelFadeTime);
            PresentationRevision++;
            if (_travelElapsed < TravelFadeTime) return;
            _fadingIn = false;
            CompleteMovement();
            return;
        }
        Events?.Advance(elapsed);
        if (_resolveAfterEvent && !IsEventRunning)
        {
            _resolveAfterEvent = false;
            ResolveTravelAfterAction();
        }
    }

    private MovementResult TryMoveWorld(Direction direction)
    {
        var from = Travel!.Snapshot.Position;
        var wasInShip = Travel.Snapshot.InShip;
        if (wasInShip) _shipFrameOffset = _shipFrameOffset % 6 + 2;
        else _walkFrameOffset = _walkFrameOffset % 12 + 2;
        var result = Travel.TryMove(MainMap!, direction, World, Party);
        PresentationRevision++;
        InteractionMessage = null;
        if (result.EntranceSceneId is { } scene)
        {
            _lastWorldEntryDirection = direction;
            RequestTravel(scene, WorldTravelRules.SceneEntry(World, scene), false);
            return new(from, from, MovementBlock.Story);
        }
        if (result.Boarded) _shipFrameOffset = 0;
        if (result.Disembarked) _walkFrameOffset = 0;
        if (result.State.Position != from) { _steps++; _arrivalPending = true; }
        var block = result.Block switch
        {
            WorldTravelBlock.None => MovementBlock.None,
            WorldTravelBlock.Boundary => MovementBlock.Boundary,
            WorldTravelBlock.Building => MovementBlock.Building,
            _ => MovementBlock.Terrain
        };
        return new(from, result.State.Position, block);
    }

    private void ResolveTravelAfterAction()
    {
        if (IsWorld || IsInputLocked || !World.HasLegacyMetadata(World.CurrentSceneId)) return;
        try
        {
            var target = WorldTravelRules.ResolveSceneTransition(World, World.CurrentSceneId, _position);
            if (target is null) return;
            if (target.ReturnWorld && (MainMap is null || Travel is null))
            {
                InteractionMessage = "此资源库尚未导入世界地图，请重新导入完整资源库。";
                return;
            }
            RequestTravel(target.SceneId, target.Position, target.ReturnWorld);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { InteractionMessage = "无法切换场景：" + error.Message; }
    }

    private void RequestTravel(int? scene, GridPosition? position, bool world)
    {
        PendingTravel = new(++_travelToken, scene, position, world);
        _travelElapsed = TimeSpan.Zero; _travelFadeFrom = FadeOpacity;
        _arrivalPending = false; _resolveAfterEvent = false;
        PresentationRevision++;
    }

    /// <summary>Host calls only after all target render resources are ready; global session state is retained.</summary>
    public bool CommitTravel(long token)
    {
        if (PendingTravel is not { } request || token != request.Token || !IsTravelReady) return false;
        if (request.IsWorld)
        {
            if (Travel is null || MainMap is null) throw new InvalidOperationException("World map data is not available.");
            var state = Travel.Snapshot;
            if (_lastWorldEntryDirection is { } entered)
            {
                // Resume the entrance's deferred terrain probe once, without detecting
                // the same entrance again. Face away when returning from the scene.
                var resumed = WorldTravelRules.Probe(MainMap, state, entered, World, Party, detectEntrances: false);
                state = resumed.State with { Facing = Opposite(entered) };
            }
            else state = state with { Facing = _facing };
            Travel.Restore(state);
            IsWorld = true;
        }
        else
        {
            var id = request.SceneId ?? throw new InvalidOperationException("Missing destination scene.");
            var position = request.Position ?? throw new InvalidOperationException("Missing scene entrance.");
            if (!SceneDefinition.Contains(position) || !World.HasLegacyMetadata(id))
                throw new InvalidOperationException("Invalid scene entrance or metadata.");
            var definition = _story!.World!.Scene(id).ToSceneDefinition(position);
            var runtime = World.ActivateScene(id);
            Definition = definition; Scene = runtime;
            if (IsWorld) _facing = Travel!.Snapshot.Facing;
            IsWorld = false; _position = position; _arrivalPending = true;
        }
        _picture = null; _walkFrameOffset = 0;
        ResetPresentationForTravel();
        PendingTravel = null; BeginTravelFadeIn();
        return true;
    }

    public bool RejectTravel(long token, string message)
    {
        if (PendingTravel?.Token != token) return false;
        PendingTravel = null; InteractionMessage = message;
        BeginTravelFadeIn();
        return true;
    }

    private void BeginTravelFadeIn()
    {
        _fadingIn = true; _travelElapsed = TimeSpan.Zero; _travelFadeFrom = FadeOpacity;
        PresentationRevision++;
    }

    private static Direction Opposite(Direction direction) => direction switch
    {
        Direction.Up => Direction.Down, Direction.Down => Direction.Up,
        Direction.Right => Direction.Left, _ => Direction.Right
    };
}
