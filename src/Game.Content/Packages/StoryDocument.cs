using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using JinYong.Core.Shops;
using System.Text.Json.Serialization;

namespace JinYong.Content.Packages;

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
    // Keep old overlays' canonical fingerprint stable when they do not add shops.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShopDefinition[]? Shops { get; init; }

    public StoryCatalog ToCatalog() => new(SceneId, InitialProgramId, InitialEventIndex, InitialPicture, InitialFacing,
        Programs.Select(program => new EventProgram(program.Id, program.Commands.Select(command =>
            new EventCommand(command.Id, command.Operation, command.Arguments, command.SourceOpcode, command.Text, command.Choices)))),
        Dialogues.Select(dialogue => new DialogueDefinition(dialogue.Id, dialogue.Pages)), ItemNames, InitialInventory, UnavailablePrograms, VariableNames,
        Party, World, InitialItemIds, Shops);
}
