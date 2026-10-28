using System.Collections.ObjectModel;

namespace JinYong.Core.Maps;

public enum WorldLayer { Earth, Surface, Building, BuildX, BuildY }

/// <summary>
/// Immutable original world map: five 480×480 signed int16 layers, not 512×512.
/// Tile pictures retain their doubled legacy IDs; owner coordinates retain all
/// signed bits. Reference: OpenLegend world/world_map.hpp and world_map.cpp.
/// </summary>
public sealed class WorldMapDefinition
{
    public const int Extent = 480, CellCount = Extent * Extent, LayerCount = 5;
    private readonly short[][] _layers;
    private readonly ReadOnlyCollection<short>[] _views;
    public WorldMapDefinition(IReadOnlyList<short[]> layers)
    {
        if (layers.Count != LayerCount || layers.Any(layer => layer.Length != CellCount))
            throw new ArgumentException("World map requires five 480×480 int16 layers.", nameof(layers));
        _layers = layers.Select(layer => layer.ToArray()).ToArray();
        _views = _layers.Select(Array.AsReadOnly).ToArray();
    }
    public static bool Contains(GridPosition position) => position.X is >= 0 and < Extent && position.Y is >= 0 and < Extent;
    public IReadOnlyList<short> Layer(WorldLayer layer)
    {
        if (!Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
        return _views[(int)layer];
    }
    public short At(WorldLayer layer, GridPosition position)
    {
        if (!Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
        if (!Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
        return _layers[(int)layer][position.Y * Extent + position.X];
    }
    public ushort RawAt(WorldLayer layer, GridPosition position) => unchecked((ushort)At(layer, position));
    // The original cache returns zero outside its range. Boat lookahead uses it.
    internal short AtOrZero(WorldLayer layer, GridPosition position) => Contains(position) ? At(layer, position) : (short)0;
}
