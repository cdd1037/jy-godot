using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using JinYong.Core.Shops;

namespace JinYong.Content.Library;

/// <summary>Global game definitions shared by all scenes. Scene maps and runtime state live elsewhere.</summary>
public sealed record GameDataDocument
{
    public int FormatVersion { get; init; } = 1;
    public EventProgramDocument[] Programs { get; init; } = [];
    public DialogueDocument[] Dialogues { get; init; } = [];
    public Dictionary<int, string> ItemNames { get; init; } = [];
    public Dictionary<int, int> InitialInventory { get; init; } = [];
    public int[] InitialItemIds { get; init; } = [];
    public PartyDefinition? Party { get; init; }
    public ShopDefinition[] Shops { get; init; } = [];
    public WorldTravelSnapshot? TravelStart { get; init; }
    public Dictionary<int, string> UnavailablePrograms { get; init; } = [];
    public Dictionary<int, string> VariableNames { get; init; } = [];
    public Dictionary<int, string> PortraitNames { get; init; } = [];

    public StoryCatalog ToStoryCatalog(int sceneId, int? initialProgramId = null, int initialEventIndex = -1,
        int? initialPicture = null, Direction initialFacing = Direction.Down, WorldDefinition? world = null)
    {
        if (FormatVersion != 1) throw new InvalidDataException($"Unsupported game data format {FormatVersion}.");
        return new(sceneId, initialProgramId, initialEventIndex, initialPicture, initialFacing,
            Programs.Select(program => new EventProgram(program.Id, program.Commands.Select(command =>
                new EventCommand(command.Id, command.Operation, command.Arguments, command.SourceOpcode, command.Text, command.Choices)))),
            Dialogues.Select(dialogue => new DialogueDefinition(dialogue.Id, dialogue.Pages)), ItemNames, InitialInventory,
            UnavailablePrograms, VariableNames, Party, world, InitialItemIds, Shops);
    }
}
