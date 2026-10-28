using JinYong.Core.Maps;

namespace JinYong.Content.Library;

/// <summary>One portable library; scenes are views over shared resources, not exported packages.</summary>
public sealed record GameLibraryManifest
{
    public int FormatVersion { get; init; } = 3;
    public string Id { get; init; } = "base";
    public string EngineApi { get; init; } = "1";
    public string Version { get; init; } = "0.3.0";
    public int DefaultScene { get; init; } = 70;
    public string SceneIndex { get; init; } = "data/scene-index.json";
    public string WorldLayers { get; init; } = "data/world.layers.bin";
    public string WorldEvents { get; init; } = "data/world.events.bin";
    public string GameData { get; init; } = "data/gameplay.json";
    public string Sprites { get; init; } = "data/sprites.json";
    public string Audio { get; init; } = "data/audio.json";
    public string SourceIndex { get; init; } = "data/source-index.json";
    public string Palette { get; init; } = "assets/palette.rgb6";
    public string? MainMap { get; init; }
    public string? Battles { get; init; }
    public string? Battlefields { get; init; }
    public Dictionary<string, string> SourceHashes { get; init; } = [];
    public Dictionary<string, string> FileHashes { get; init; } = [];
}

public sealed record LibraryScene
{
    public int SceneId { get; init; }
    public string Name { get; init; } = "";
    public bool HasLegacyMetadata { get; init; }
    public short[] Metadata { get; init; } = [];
    public int SpawnX { get; init; }
    public int SpawnY { get; init; }
    public string? SpriteBank { get; init; }
}

public sealed record SourceResource(string Sha256, long Size, string Storage, string Kind);
public sealed record LibraryAudio
{
    public Dictionary<int, string> Effects { get; init; } = [];
    public Dictionary<int, string> Attacks { get; init; } = [];
    public Dictionary<int, string> Music { get; init; } = [];
}

