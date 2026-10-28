using System.Collections.ObjectModel;

namespace JinYong.Core.Maps;

public sealed class SceneEventDefinition
{
    private readonly short[] _words;
    public SceneEventDefinition(IEnumerable<short> words)
    {
        _words = words.ToArray();
        if (_words.Length != 11) throw new ArgumentException("A legacy scene event has 11 words.");
    }

    public short this[int field] => _words[field];
    public bool BlocksMovement => _words[0] != 0;
    public int Picture => _words[5] > 0 ? _words[7] : 0;
}

public interface ISceneMap
{
    string Id { get; }
    string Name { get; }
    GridPosition Spawn { get; }
    IReadOnlyList<SceneEventDefinition> Events { get; }
    short At(MapLayer layer, GridPosition position);
    SceneEventDefinition? EventAt(GridPosition position);
}

public sealed class SceneDefinition : ISceneMap
{
    public const int Extent = 64;
    public const int LayerCount = 6;
    public const int TileCount = Extent * Extent;
    private readonly short[] _layers;
    private readonly ReadOnlyCollection<SceneEventDefinition> _events;

    public SceneDefinition(string id, string name, GridPosition spawn, IEnumerable<short> layers,
        IEnumerable<SceneEventDefinition> events)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Scene ID is required.");
        Id = id;
        Name = name;
        Spawn = spawn;
        _layers = layers.ToArray();
        _events = Array.AsReadOnly(events.ToArray());
        if (_layers.Length != LayerCount * TileCount)
            throw new ArgumentException("Scene must contain six 64 by 64 layers.");
        if (_events.Count != 200) throw new ArgumentException("Scene must contain 200 events.");
        if (!Contains(spawn)) throw new ArgumentException("Spawn is outside the scene.");
        for (var i = 3 * TileCount; i < 4 * TileCount; i++)
            if (_layers[i] < -1 || _layers[i] >= _events.Count)
                throw new ArgumentException($"Invalid scene event reference {_layers[i]}.");
    }

    public string Id { get; }
    public string Name { get; }
    public GridPosition Spawn { get; }
    public IReadOnlyList<SceneEventDefinition> Events => _events;
    public static bool Contains(GridPosition position) =>
        position.X is >= 0 and < Extent && position.Y is >= 0 and < Extent;

    public short At(MapLayer layer, GridPosition position)
    {
        if (!Contains(position) || !Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(position));
        return _layers[(int)layer * TileCount + position.Y * Extent + position.X];
    }

    public SceneEventDefinition? EventAt(GridPosition position)
    {
        var index = At(MapLayer.Event, position);
        return index < 0 ? null : _events[index];
    }
}
