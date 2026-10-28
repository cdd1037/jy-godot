using JinYong.Core.Maps;
using JinYong.Core.Events;
using System.Collections.ObjectModel;
using JinYong.Core.Battles;

namespace JinYong.Core.Session;

public readonly record struct PlayerView(GridPosition Position, Direction Facing, long Steps, int? PictureOverride = null,
    int WalkFrameOffset = 0);
public readonly record struct MovementResult(GridPosition From, GridPosition To, MovementBlock Block)
{
    public bool Moved => Block == MovementBlock.None && From != To;
}

public sealed partial class GameSession : IEventWorld
{
    private GridPosition _position;
    private Direction _facing = Direction.Down;
    private long _steps;
    private int? _picture;
    private int _walkFrameOffset;
    private bool _arrivalPending;
    private readonly Dictionary<int, int> _inventory = [];
    private readonly HashSet<int> _itemIds = [];
    private readonly Dictionary<string, int> _variables = new(StringComparer.Ordinal);
    private readonly StoryCatalog? _story;

    public GameSession(SceneDefinition scene, StoryCatalog? story = null, bool skipOpening = false,
        WorldMapDefinition? worldMap = null, WorldTravelSnapshot? travelStart = null, BattleCatalog? battles = null,
        bool startInitialProgram = true, int? sceneIdOverride = null, bool validateSpawn = true)
    {
        Definition = scene;
        Scene = new SceneRuntimeState(scene);
        _story = story;
        World = new SceneWorldState(sceneIdOverride ?? story?.SceneId ?? 0, Scene, story?.World);
        Party = story?.Party is { } party ? new PartyState(party) : null;
        MainMap = worldMap;
        BattleResources = battles;
        Travel = worldMap is not null && travelStart is not null ? new WorldTravelState(travelStart) : null;
        _position = scene.Spawn;
        var block = SceneMovementRules.Check(scene, _position, _position);
        if (validateSpawn && block != MovementBlock.None)
            throw new ArgumentException($"Spawn {scene.Spawn} is blocked ({block}). Specify another spawn.");
        if (story is not null)
        {
            foreach (var pair in story.InitialInventory) _inventory.Add(pair.Key, pair.Value);
            _itemIds.UnionWith(story.InitialItemIds);
            _facing = story.InitialFacing;
            _picture = story.InitialPicture;
            Events = new EventRunner(story, this);
            if (startInitialProgram && story.InitialProgramId is { } initial)
            {
                if (!Events.TryStart(initial, story.InitialEventIndex, scene.Spawn))
                    throw new InvalidOperationException(Events.Error ?? "Initial event cannot start.");
                if (skipOpening) CompleteOpeningForTesting();
            }
        }
    }

    private void CompleteOpeningForTesting()
    {
        // Use the same event runner so testing starts with the actual post-opening
        // state, including event changes and inventory, without visible prompts.
        var events = Events!;
        for (var budget = 0; events.IsRunning && events.Error is null && budget < 10000; budget++)
        {
            if (events.PendingLifecycle is not null)
                throw new InvalidOperationException("Testing startup cannot replace a session through a death/load request. Use -PlayOpening for this content.");
            if (events.Pending?.Kind == EventPresentationKind.Choice)
                throw new InvalidOperationException("Testing startup cannot choose a story branch. Use -PlayOpening for this content.");
            if (events.Pending is { } pending) events.Confirm(pending.Token);
            else events.Advance(TimeSpan.FromSeconds(1));
        }
        if (events.Error is not null || events.IsRunning)
            throw new InvalidOperationException(events.Error ?? "Opening could not finish within the testing startup limit.");
        events.TakeSounds();
    }

    public SceneDefinition Definition { get; private set; }
    public SceneRuntimeState Scene { get; private set; }
    public SceneWorldState World { get; }
    public PartyState? Party { get; }
    public EventRunner? Events { get; }
    public StoryCatalog? Story => _story;
    public bool IsEventRunning => Events?.IsRunning == true;
    public long PresentationRevision { get; private set; }
    public double FadeOpacity { get; private set; }
    public PlayerView Player => IsWorld && Travel is { } travel
        ? new(travel.Snapshot.Position, travel.Snapshot.InShip ? travel.Snapshot.ShipFacing : travel.Snapshot.Facing,
            _steps, WorldPlayerPicture, _walkFrameOffset)
        : new(_position, _facing, _steps, _picture, _walkFrameOffset);
    public IReadOnlyDictionary<int, int> Inventory => new ReadOnlyDictionary<int, int>(_inventory);
    public IReadOnlyDictionary<string, int> Variables => new ReadOnlyDictionary<string, int>(_variables);
    public string? InteractionMessage { get; private set; }

    public MovementResult TryMove(Direction direction)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (IsInputLocked) return new(Player.Position, Player.Position, MovementBlock.Story);
        if (IsWorld) return TryMoveWorld(direction);
        _facing = direction;
        _picture = null;
        _walkFrameOffset = _walkFrameOffset % 12 + 2;
        PresentationRevision++;
        var target = _position.Step(direction);
        var block = SceneMovementRules.Check(Scene, _position, target);
        var from = _position;
        if (block == MovementBlock.None) { _position = target; _steps++; _arrivalPending = true; ResetCameraAfterMovement(); }
        return new MovementResult(from, _position, block);
    }

    public void CompleteMovement()
    {
        if (!_arrivalPending || IsInputLocked) return;
        _arrivalPending = false;
        if (IsWorld) return;
        var point = Scene.EventAt(_position);
        if (point is not null && point[4] > 0 && StartEvent(point[4], Scene.At(MapLayer.Event, _position), _position) && IsEventRunning) return;
        ResolveTravelAfterAction();
    }

    internal void StandAfterWalking()
    {
        if (IsInputLocked || _walkFrameOffset == 0) return;
        _walkFrameOffset = 0;
        PresentationRevision++;
    }

    public bool Interact()
    {
        InteractionMessage = null;
        if (IsInputLocked || IsWorld) return false;
        var target = _position.Step(_facing);
        if (!SceneDefinition.Contains(target) || Scene.EventAt(target) is not { } point || point[2] <= 0) return false;
        return StartEvent(point[2], Scene.At(MapLayer.Event, target), target);
    }

    public bool UseItem(int itemId)
    {
        InteractionMessage = null;
        if (IsInputLocked || IsWorld) return false;
        if (_inventory.GetValueOrDefault(itemId) <= 0) { InteractionMessage = "行囊中没有这件物品。"; return false; }
        var target = _position.Step(_facing);
        if (!SceneDefinition.Contains(target) || Scene.EventAt(target) is not { } point || point[3] <= 0)
        { InteractionMessage = "这里无法使用这件物品。"; return false; }
        return StartEvent(point[3], Scene.At(MapLayer.Event, target), target, itemId);
    }

    private bool StartEvent(int programId, int eventIndex, GridPosition target, int itemId = -1)
    {
        if (Events is null || _story?.Programs.ContainsKey(programId) != true)
        {
            InteractionMessage = "这个物件的功能尚未开放。" + _story?.UnavailablePrograms.GetValueOrDefault(programId);
            return false;
        }
        var previousPicture = _picture;
        var previousWalkFrame = _walkFrameOffset;
        _picture = null;
        _walkFrameOffset = 0;
        if (!Events.TryStart(programId, eventIndex, target, itemId))
        { _picture = previousPicture; _walkFrameOffset = previousWalkFrame; InteractionMessage = Events.Error ?? "事件暂时无法启动。"; return false; }
        _resolveAfterEvent = true;
        PresentationRevision++;
        return true;
    }

    void IEventWorld.ChangeEvent(int context, GridPosition trigger, IReadOnlyList<int> arguments) =>
        World.ChangeEvent(World.CurrentSceneId, context, trigger, arguments);
    void IEventWorld.Face(Direction direction) { _facing = direction; _picture = null; _walkFrameOffset = 0; PresentationRevision++; }
    void IEventWorld.SetFade(double opacity)
    {
        FadeOpacity = Math.Clamp(opacity, 0, 1);
        PresentationRevision++;
    }
    void IEventWorld.SetPicture(int eventIndex, int picture)
    {
        if (eventIndex == -1) { _picture = picture; PresentationRevision++; }
        else Scene.SetEventPicture(eventIndex, picture);
    }
    void IEventWorld.AddItem(int id, int count)
    {
        if (_story?.ItemNames.ContainsKey(id) != true) throw new ArgumentException($"Unknown item {id}.");
        var quantity = checked(_inventory.GetValueOrDefault(id) + count);
        if (quantity < 0) throw new ArgumentException("Insufficient item quantity.");
        if (quantity == 0) _inventory.Remove(id); else _inventory[id] = quantity;
        if (quantity == 0) _itemIds.Remove(id); else _itemIds.Add(id);
        PresentationRevision++;
    }
    int IEventWorld.ItemCount(int id) => _inventory.GetValueOrDefault(id);
    bool IEventWorld.ItemExists(int id) => _itemIds.Contains(id);
    int IEventWorld.GetVariable(int id) => _variables.GetValueOrDefault(_story!.VariableNames[id]);
    void IEventWorld.SetVariable(int id, int value) => _variables[_story!.VariableNames[id]] = value;
    int IEventWorld.EventField(int context, IReadOnlyList<int> args)
    {
        var index = args[1] is -1 or -2 ? context : args[1];
        return World.EventField(args[0], index, args[2]);
    }
    void IEventWorld.SetTile(IReadOnlyList<int> args) => World.SetTile(args[0], (MapLayer)args[1], new(args[2], args[3]), args[4]);
    void IEventWorld.Teleport(GridPosition position)
    {
        _position = position; _arrivalPending = false; _picture = null; ResetCameraAfterMovement(); PresentationRevision++;
    }
    void IEventWorld.ValidateCommand(EventCommand command, int context)
    {
        var args = command.Arguments;
        switch (command.Operation)
        {
            case EventOperation.ChangeEvent: World.ValidateChangeEvent(World.CurrentSceneId, context, args, validateCurrentPosition: false); break;
            case EventOperation.Animate:
                if (args[0] < -1 || args[0] >= Scene.Events.Count) throw new ArgumentException("Unknown animation event.");
                break;
            case EventOperation.SetTile:
                World.Scene(args[0]).ValidateTile((MapLayer)args[1], new(args[2], args[3]), args[4]); break;
            case EventOperation.Teleport:
                if (!SceneDefinition.Contains(new(args[0], args[1]))) throw new ArgumentException("Player destination is outside the scene.");
                break;
            case EventOperation.BranchEvent:
                var index = args[1] is -1 or -2 ? context : args[1];
                _ = World.EventField(args[0], index, args[2]);
                break;
        }
        ValidateGameCommand(command, context);
        ValidatePresentationCommand(command);
        ValidateBattleCommand(command);
    }
}
