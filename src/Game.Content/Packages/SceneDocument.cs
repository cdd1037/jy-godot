using System.Text.Json.Serialization;

namespace JinYong.Content.Packages;

public sealed record SpriteAsset
{
    public int LegacyId { get; init; }
    public string Atlas { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
}

public sealed record SceneDocument
{
    public int FormatVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public int LegacySceneId { get; init; }
    public string Name { get; init; } = "";
    public int SpawnX { get; init; }
    public int SpawnY { get; init; }
    public string Layers { get; init; } = "layers.bin";
    public string Palette { get; init; } = "palette.rgb6";
    public string LegacyMetadata { get; init; } = "";
    public short[][] Events { get; init; } = [];
    public SpriteAsset[] Sprites { get; init; } = [];
    public string? Story { get; init; }
    public SpriteAsset[] Portraits { get; init; } = [];
    public Dictionary<int, string> PortraitNames { get; init; } = [];
    public Dictionary<int, string> Sounds { get; init; } = [];
    // Runtime-only views over the central catalog. FIGHT IDs are local to each
    // head bank and must never be merged into the battlefield sprite dictionary.
    [JsonIgnore]
    public IReadOnlyDictionary<string, SpriteAsset[]> BattleSpriteBanks { get; init; } =
        new Dictionary<string, SpriteAsset[]>();
}
