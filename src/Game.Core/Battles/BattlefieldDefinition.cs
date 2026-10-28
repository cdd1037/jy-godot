using System.Text.Json.Serialization;
using JinYong.Core.Maps;

namespace JinYong.Core.Battles;

public enum BattlefieldLayer { Ground, Building }

/// <summary>Immutable WARFLD map: two x-fast 64×64 signed-word layers; pictures retain doubled IDs.</summary>
public sealed class BattlefieldDefinition
{
    public const int Extent = 64, TileCount = Extent * Extent, LayerCount = 2;

    [JsonConstructor]
    public BattlefieldDefinition(int id, IReadOnlyList<short> layers)
    {
        if (id < 0 || layers.Count != TileCount * LayerCount)
            throw new ArgumentException("Battlefield requires a nonnegative ID and two 64 by 64 layers.");
        Id = id;
        Layers = Array.AsReadOnly(layers.ToArray());
    }
    public int Id { get; }
    public IReadOnlyList<short> Layers { get; }
    public static bool Contains(GridPosition position) => position.X is >= 0 and < Extent && position.Y is >= 0 and < Extent;
    public short At(BattlefieldLayer layer, GridPosition position)
    {
        if (!Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
        if (!Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
        return Layers[(int)layer * TileCount + position.Y * Extent + position.X];
    }
}
