using System.Text.Json;
using System.Text.Json.Serialization;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Content.Packages;

public sealed record PackageManifest
{
    public int FormatVersion { get; init; } = 1;
    public string Id { get; init; } = "base";
    public string EngineApi { get; init; } = "1";
    public string Version { get; init; } = "0.1.0";
    public string Scene { get; init; } = "scene.json";
    public Dictionary<string, string> SourceHashes { get; init; } = [];
    public Dictionary<string, string> FileHashes { get; init; } = [];
}

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

public sealed record EventCommandDocument(int Id, EventOperation Operation, int[] Arguments, int SourceOpcode = -1,
    string? Text = null, EventChoice[]? Choices = null);
public sealed record EventProgramDocument(int Id, EventCommandDocument[] Commands);
public sealed record DialogueDocument(int Id, string[] Pages);
public sealed record StoryDocument
{
    public int FormatVersion { get; init; } = 1;
    public int SceneId { get; init; }
    public int? InitialProgramId { get; init; }
    public int InitialEventIndex { get; init; }
    public int? InitialPicture { get; init; }
    public Direction InitialFacing { get; init; } = Direction.Down;
    public EventProgramDocument[] Programs { get; init; } = [];
    public DialogueDocument[] Dialogues { get; init; } = [];
    public Dictionary<int, string> ItemNames { get; init; } = [];
    public Dictionary<int, int> InitialInventory { get; init; } = [];
    public Dictionary<int, string> UnavailablePrograms { get; init; } = [];
    public Dictionary<int, string> VariableNames { get; init; } = [];
    public PartyDefinition? Party { get; init; }
    public WorldDefinition? World { get; init; }
    public int[]? InitialItemIds { get; init; }

    public StoryCatalog ToCatalog() => new(SceneId, InitialProgramId, InitialEventIndex, InitialPicture, InitialFacing,
        Programs.Select(program => new EventProgram(program.Id, program.Commands.Select(command =>
            new EventCommand(command.Id, command.Operation, command.Arguments, command.SourceOpcode, command.Text, command.Choices)))),
        Dialogues.Select(dialogue => new DialogueDefinition(dialogue.Id, dialogue.Pages)), ItemNames, InitialInventory, UnavailablePrograms, VariableNames,
        Party, World, InitialItemIds);
}

public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty content document: {path}");
}
