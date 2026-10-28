using JinYong.Content.Packages;

namespace JinYong.Content.Library;

/// <summary>
/// Library-wide atlas assets and local-ID indirection. Asset keys are SHA-256 of
/// the decoded frame dimensions, anchor, indices and alpha, not original file names.
/// A view uses Assets[Banks[bank][localId]] with { LegacyId = localId }.
/// JSON names follow ContentJson.Options; scene banks are scene/000, scene/001, etc.
/// </summary>
public sealed record SharedSpriteCatalog
{
    public Dictionary<string, SpriteAsset> Assets { get; init; } = [];
    public Dictionary<string, Dictionary<int, string>> Banks { get; init; } = [];
}
