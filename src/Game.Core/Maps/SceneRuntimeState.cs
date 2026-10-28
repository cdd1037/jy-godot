using System.Collections.ObjectModel;

namespace JinYong.Core.Maps;

public sealed partial class SceneRuntimeState : ISceneMap
{
    private readonly SceneDefinition _definition;
    private readonly SceneEventDefinition[] _events;
    private readonly ReadOnlyCollection<SceneEventDefinition> _eventView;
    private readonly Dictionary<(MapLayer, GridPosition), short> _tiles = [];

    public SceneRuntimeState(SceneDefinition definition)
    {
        _definition = definition;
        _events = definition.Events.ToArray();
        _eventView = Array.AsReadOnly(_events);
    }

    public string Id => _definition.Id;
    public string Name => _definition.Name;
    public GridPosition Spawn => _definition.Spawn;
    public IReadOnlyList<SceneEventDefinition> Events => _eventView;
    public long Revision { get; private set; }
    public short At(MapLayer layer, GridPosition position) =>
        _tiles.TryGetValue((layer, position), out var value) ? value : _definition.At(layer, position);
    public SceneEventDefinition? EventAt(GridPosition position)
    {
        var index = At(MapLayer.Event, position);
        return index < 0 ? null : _events[index];
    }

    internal void ChangeEvent(int sceneId, int eventContext, GridPosition triggerPosition, IReadOnlyList<int> arguments)
    {
        ValidateChangeEvent(sceneId, eventContext, arguments);
        if (arguments.Count != 13) throw new ArgumentException("An event change requires 13 arguments.");
        var targetScene = arguments[0] == -2 ? sceneId : arguments[0];
        if (targetScene != sceneId) throw new InvalidOperationException("The target scene is not loaded.");
        var index = arguments[1] is -1 or -2 ? eventContext : arguments[1];
        if (index < 0 || index >= _events.Length) throw new ArgumentException("Event target is outside the scene.");
        var previous = _events[index];
        var words = Enumerable.Range(0, 11).Select(field => previous[field]).ToArray();
        for (var field = 0; field < 11; field++)
            if (arguments[field + 2] != -2) words[field] = checked((short)arguments[field + 2]);
        var oldPoint = new GridPosition(previous[9], previous[10]);
        var newPoint = new GridPosition(words[9], words[10]);
        var relocates = arguments[11] != -2 || arguments[12] != -2;
        if (relocates && !SceneDefinition.Contains(newPoint)) throw new ArgumentException("Event position is outside the scene.");
        if (arguments[1] == -1 && SceneDefinition.Contains(triggerPosition)) _tiles[(MapLayer.Event, triggerPosition)] = -1;
        if (relocates)
        {
            if (SceneDefinition.Contains(oldPoint)) _tiles[(MapLayer.Event, oldPoint)] = -1;
            _tiles[(MapLayer.Event, newPoint)] = (short)index;
        }
        _events[index] = new SceneEventDefinition(words);
        Revision++;
    }

    internal void ValidateChangeEvent(int sceneId, int eventContext, IReadOnlyList<int> arguments, bool validateCurrentPosition = true)
    {
        if (arguments.Count != 13) throw new ArgumentException("An event change requires 13 arguments.");
        if (arguments[0] != -2 && arguments[0] != sceneId) throw new InvalidOperationException("The target scene is not loaded.");
        var index = arguments[1] is -1 or -2 ? eventContext : arguments[1];
        if (index < 0 || index >= _events.Length) throw new ArgumentException("Event target is outside the scene.");
        foreach (var value in arguments.Skip(2)) _ = checked((short)value);
        if (arguments[11] != -2 || arguments[12] != -2)
        {
            if (arguments[11] != -2 && arguments[11] is < 0 or >= 64 || arguments[12] != -2 && arguments[12] is < 0 or >= 64)
                throw new ArgumentException("Event position is outside the scene.");
            var x = arguments[11] == -2 ? _events[index][9] : arguments[11];
            var y = arguments[12] == -2 ? _events[index][10] : arguments[12];
            if (validateCurrentPosition && !SceneDefinition.Contains(new(x, y))) throw new ArgumentException("Event position is outside the scene.");
        }
    }

    internal void ValidateTile(MapLayer layer, GridPosition position, int value)
    {
        if (!Enum.IsDefined(layer) || !SceneDefinition.Contains(position)) throw new ArgumentException("Tile target is outside the scene.");
        _ = checked((short)value);
        if (layer == MapLayer.Event && (value < -1 || value >= _events.Length)) throw new ArgumentException("Invalid event tile.");
    }

    internal void SetTile(MapLayer layer, GridPosition position, int value)
    {
        ValidateTile(layer, position, value);
        _tiles[(layer, position)] = (short)value;
        Revision++;
    }

    internal void SetEventPicture(int index, int picture)
    {
        if (index < 0 || index >= _events.Length) throw new ArgumentException("Animation event is outside the scene.");
        var words = Enumerable.Range(0, 11).Select(field => _events[index][field]).ToArray();
        words[5] = words[6] = words[7] = checked((short)picture);
        _events[index] = new SceneEventDefinition(words);
        Revision++;
    }

    internal void SetEventField(int index, int field, int value)
    {
        if (index < 0 || index >= _events.Length || field is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(index));
        var words = Enumerable.Range(0, 11).Select(word => _events[index][word]).ToArray();
        words[field] = checked((short)value);
        _events[index] = new SceneEventDefinition(words);
        Revision++;
    }
}
