using System.Collections.ObjectModel;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using JinYong.Core.Shops;

namespace JinYong.Core.Events;

public enum EventOperation
{
    Refresh, Dialogue, ChangeEvent, Animate, Face, Sound, AddItem, RandomDialogue, End,
    Jump, BranchItem, BranchUsedItem, BranchVariable, BranchEvent, BranchAllItems,
    Choice, Call, Return, Stop, Wait, SetVariable, AddVariable, AdjustItem, SetTile, Teleport,
    JoinParty, LeaveParty, RestParty, ClearPartyMp, SetRoleAttribute, AddRoleAttribute,
    BranchRole, LearnMagic, SetMagic, RoleAddItem, NoticeRole, BranchParty, BranchPartyFull,
    BranchPartyFemale, DismissParty, AddFame, IncrementEventScripts, ReplaceTiles, OpenScene,
    OpenAllScenes, BranchBooks, MoveShop, BranchItemExists, Fade, PanCamera, ScriptedWalk, AnimatePair, Battle,
    Death, LoadMenu, ExitMusic, Music, StatueAnimation, TournamentTrials, Ending, Shop
}
public enum EventComparison { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }
public sealed record EventChoice(string Text, int Target);

public sealed class EventCommand
{
    private readonly ReadOnlyCollection<int> _arguments;
    public EventCommand(int id, EventOperation operation, IEnumerable<int> arguments, int sourceOpcode = -1,
        string? text = null, IEnumerable<EventChoice>? choices = null)
    {
        Id = id;
        Operation = operation;
        _arguments = Array.AsReadOnly(arguments.ToArray());
        var expected = operation switch
        {
            EventOperation.Refresh or EventOperation.End or EventOperation.Return or EventOperation.Stop or
                EventOperation.RestParty or EventOperation.ClearPartyMp or EventOperation.DismissParty or
                EventOperation.OpenAllScenes or EventOperation.MoveShop or EventOperation.LoadMenu or
                EventOperation.StatueAnimation or EventOperation.TournamentTrials or EventOperation.Shop => 0,
            EventOperation.Dialogue or EventOperation.Animate or EventOperation.SetRoleAttribute or
                EventOperation.AddRoleAttribute or EventOperation.LearnMagic or EventOperation.RoleAddItem or
                EventOperation.BranchParty or EventOperation.BranchItemExists => 3,
            EventOperation.ChangeEvent => 13,
            EventOperation.Face or EventOperation.Sound or EventOperation.Jump or EventOperation.Call or EventOperation.Wait or EventOperation.Choice or
                EventOperation.JoinParty or EventOperation.LeaveParty or EventOperation.AddFame or EventOperation.OpenScene or EventOperation.Death or
                EventOperation.ExitMusic or EventOperation.Music => 1,
            EventOperation.AddItem or EventOperation.AdjustItem or EventOperation.SetVariable or EventOperation.AddVariable or EventOperation.Teleport or
                EventOperation.NoticeRole or EventOperation.BranchPartyFull or EventOperation.BranchPartyFemale or EventOperation.BranchBooks or EventOperation.Fade => 2,
            EventOperation.BranchUsedItem => 3,
            EventOperation.RandomDialogue or EventOperation.BranchItem or EventOperation.SetMagic or EventOperation.ReplaceTiles or
                EventOperation.PanCamera or EventOperation.ScriptedWalk or EventOperation.Battle => 4,
            EventOperation.BranchVariable or EventOperation.SetTile or EventOperation.IncrementEventScripts or EventOperation.AnimatePair or EventOperation.Ending => 5,
            EventOperation.BranchEvent or EventOperation.BranchRole => 6,
            EventOperation.BranchAllItems => 7,
            _ => throw new ArgumentException("Unknown event operation.")
        };
        if (_arguments.Count != expected) throw new ArgumentException($"{operation} needs {expected} arguments.");
        if (id < 0) throw new ArgumentException("Command IDs must be nonnegative.");
        Text = text;
        Choices = Array.AsReadOnly((choices ?? []).ToArray());
        if (operation == EventOperation.Choice)
        {
            if (string.IsNullOrWhiteSpace(text) || Choices.Count is < 2 or > 6 || Choices.Any(choice => string.IsNullOrWhiteSpace(choice.Text)))
                throw new ArgumentException("A choice needs a prompt and two to six labeled options.");
        }
        else if (Choices.Count != 0) throw new ArgumentException("Only choice commands have options.");
        if (operation == EventOperation.Wait && _arguments[0] is < 0 or > 3600000)
            throw new ArgumentException("Wait duration must be between zero and one hour in milliseconds.");
        if (operation == EventOperation.Fade && (_arguments[0] is < 0 or > 1 || _arguments[1] is < 0 or > 60000))
            throw new ArgumentException("Fade needs a black target of zero or one and a duration between zero and 60000 milliseconds.");
        if (operation == EventOperation.Face && _arguments[0] is < 0 or > 3)
            throw new ArgumentException("Invalid facing direction.");
        if (operation == EventOperation.Battle && (_arguments[0] < 0 || _arguments[3] is < short.MinValue or > short.MaxValue))
            throw new ArgumentException("Battle needs a nonnegative definition ID and a signed reward flag.");
        if (operation == EventOperation.Death && _arguments[0] is < short.MinValue or > short.MaxValue)
            throw new ArgumentException("Death preserves one signed source word; the original callee ignores it.");
        if ((operation is EventOperation.ExitMusic or EventOperation.Music) &&
            _arguments[0] is < short.MinValue or > short.MaxValue)
            throw new ArgumentException("Music identifiers must fit a signed source word.");
        if (operation == EventOperation.BranchVariable && !Enum.IsDefined((EventComparison)_arguments[1]))
            throw new ArgumentException("Invalid variable comparison.");
        if (operation == EventOperation.BranchItem && _arguments[1] < 0) throw new ArgumentException("Negative required item count.");
        if (operation == EventOperation.RandomDialogue && _arguments[1] is < 2 or > 30000)
            throw new ArgumentException("Invalid random dialogue range.");
        if (operation == EventOperation.Animate &&
            (_arguments[0] is < -1 or >= 200 || _arguments[1] < 0 || _arguments[2] < _arguments[1] ||
             _arguments[2] > 32766 || (_arguments[1] & 1) != 0 || (_arguments[2] & 1) != 0))
            throw new ArgumentException("Invalid animation target or frame range.");
        if (operation is EventOperation.PanCamera or EventOperation.ScriptedWalk)
        {
            if (_arguments.Any(value => value is < short.MinValue or > short.MaxValue) ||
                Math.Abs((long)_arguments[2] - _arguments[0]) + Math.Abs((long)_arguments[3] - _arguments[1]) > 4096)
                throw new ArgumentException("Scene motion needs signed 16-bit coordinates and at most 4096 steps.");
        }
        if ((operation is EventOperation.AnimatePair or EventOperation.Ending) &&
            (_arguments[0] is < -1 or >= 200 || _arguments[3] is < -1 or >= 200 ||
             new[] { _arguments[1], _arguments[2], _arguments[4] }.Any(value => value is < short.MinValue or > short.MaxValue) ||
             ((long)_arguments[2] - _arguments[1]) / 2 + 1 > 4096))
            throw new ArgumentException("Invalid paired animation targets, signed picture words or frame budget.");
        SourceOpcode = sourceOpcode;
    }
    public int Id { get; }
    public EventOperation Operation { get; }
    public IReadOnlyList<int> Arguments => _arguments;
    public int SourceOpcode { get; }
    public string? Text { get; }
    public IReadOnlyList<EventChoice> Choices { get; }
}

public sealed class EventProgram
{
    public EventProgram(int id, IEnumerable<EventCommand> commands)
    {
        if (id < 0) throw new ArgumentException("Program IDs must be nonnegative.");
        Id = id;
        Commands = Array.AsReadOnly(commands.ToArray());
        if (Commands.Count == 0 || Commands[^1].Operation is not (EventOperation.End or EventOperation.Return or EventOperation.Stop or EventOperation.Death or EventOperation.LoadMenu or EventOperation.Ending))
            throw new ArgumentException($"Program {id} must end explicitly.");
        if (Commands.Select(command => command.Id).Distinct().Count() != Commands.Count)
            throw new ArgumentException($"Program {id} has duplicate command IDs.");
        _indices = Commands.Select((command, index) => (command.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        foreach (var command in Commands)
        foreach (var target in Targets(command))
            if (!_indices.ContainsKey(target)) throw new ArgumentException($"Program {id}, command {command.Id}: target {target} is not a command boundary.");
    }
    private readonly Dictionary<int, int> _indices;
    public int Id { get; }
    public IReadOnlyList<EventCommand> Commands { get; }
    public int IndexOf(int commandId) => _indices.TryGetValue(commandId, out var index) ? index
        : throw new InvalidOperationException($"Unknown command target {commandId} in program {Id}.");
    public static IEnumerable<int> Targets(EventCommand command) => command.Operation switch
    {
        EventOperation.Jump => command.Arguments,
        EventOperation.Battle => command.Arguments.Skip(1).Take(2),
        EventOperation.BranchItem or EventOperation.BranchUsedItem or EventOperation.BranchVariable or EventOperation.BranchEvent or EventOperation.BranchAllItems or
            EventOperation.BranchRole or EventOperation.BranchParty or EventOperation.BranchPartyFull or EventOperation.BranchPartyFemale or
            EventOperation.BranchBooks or EventOperation.BranchItemExists => command.Arguments.TakeLast(2),
        EventOperation.Choice => command.Choices.Select(choice => choice.Target).Concat(command.Arguments.Where(target => target != -1)),
        _ => []
    };
}

public sealed class DialogueDefinition
{
    public DialogueDefinition(int id, IEnumerable<string> pages)
    {
        if (id < 0) throw new ArgumentException("Dialogue IDs must be nonnegative.");
        Id = id;
        Pages = Array.AsReadOnly(pages.ToArray());
        if (Pages.Count == 0) throw new ArgumentException("Dialogue needs at least one page.");
    }
    public int Id { get; }
    public IReadOnlyList<string> Pages { get; }
}

public sealed class StoryCatalog
{
    public StoryCatalog(int sceneId, int? initialProgramId, int initialEventIndex, int? initialPicture,
        Direction initialFacing, IEnumerable<EventProgram> programs, IEnumerable<DialogueDefinition> dialogues,
        IReadOnlyDictionary<int, string> itemNames, IReadOnlyDictionary<int, int> initialInventory,
        IReadOnlyDictionary<int, string>? unavailablePrograms = null, IReadOnlyDictionary<int, string>? variableNames = null,
        PartyDefinition? party = null, WorldDefinition? world = null, IReadOnlyCollection<int>? initialItemIds = null,
        IEnumerable<ShopDefinition>? shops = null)
    {
        SceneId = sceneId;
        InitialProgramId = initialProgramId;
        InitialEventIndex = initialEventIndex;
        InitialPicture = initialPicture;
        InitialFacing = initialFacing;
        Party = party;
        World = world;
        InitialItemIds = Array.AsReadOnly((initialItemIds ?? initialInventory.Keys.ToArray()).Distinct().ToArray());
        if (InitialItemIds.Any(id => !itemNames.ContainsKey(id))) throw new ArgumentException("Inventory slots reference an unknown item.");
        Programs = new ReadOnlyDictionary<int, EventProgram>(programs.ToDictionary(program => program.Id));
        Dialogues = new ReadOnlyDictionary<int, DialogueDefinition>(dialogues.ToDictionary(dialogue => dialogue.Id));
        ItemNames = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(itemNames));
        var shopDefinitions = (shops ?? []).ToArray();
        if (shopDefinitions.Select(shop => shop.Id).Distinct().Count() != shopDefinitions.Length ||
            shopDefinitions.Select(shop => shop.SceneId).Distinct().Count() != shopDefinitions.Length)
            throw new ArgumentException("Shops require unique IDs and scene IDs.");
        if (shopDefinitions.SelectMany(shop => shop.Offers).Any(offer => !ItemNames.ContainsKey(offer.ItemId)))
            throw new ArgumentException("Shop offers reference an unknown item.");
        Shops = new ReadOnlyDictionary<int, ShopDefinition>(shopDefinitions.ToDictionary(shop => shop.Id));
        InitialInventory = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(initialInventory));
        UnavailablePrograms = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(unavailablePrograms ?? new Dictionary<int, string>()));
        VariableNames = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(variableNames ?? new Dictionary<int, string>()));
        if (VariableNames.Keys.Any(id => id < 0) || VariableNames.Values.Any(name => string.IsNullOrWhiteSpace(name) ||
                name.IndexOf(':') <= 0 || name.EndsWith(':')) ||
            VariableNames.Values.Distinct(StringComparer.Ordinal).Count() != VariableNames.Count)
            throw new ArgumentException("Variable names must be unique and namespaced, for example demo:quest/key.");
        if (InitialInventory.Any(pair => pair.Value <= 0 || !ItemNames.ContainsKey(pair.Key)) || ItemNames.Keys.Any(id => id < 0))
            throw new ArgumentException("Initial inventory must contain known items with positive quantities.");
        if (initialProgramId.HasValue && !Programs.ContainsKey(initialProgramId.Value))
            throw new ArgumentException("Initial event program is unavailable.");
        EventCatalogValidator.Validate(this);
    }
    public int SceneId { get; }
    public int? InitialProgramId { get; }
    public int InitialEventIndex { get; }
    public int? InitialPicture { get; }
    public Direction InitialFacing { get; }
    public IReadOnlyDictionary<int, EventProgram> Programs { get; }
    public IReadOnlyDictionary<int, DialogueDefinition> Dialogues { get; }
    public IReadOnlyDictionary<int, string> ItemNames { get; }
    public IReadOnlyDictionary<int, ShopDefinition> Shops { get; }
    public IReadOnlyDictionary<int, int> InitialInventory { get; }
    public IReadOnlyDictionary<int, string> UnavailablePrograms { get; }
    public IReadOnlyDictionary<int, string> VariableNames { get; }
    public PartyDefinition? Party { get; }
    public WorldDefinition? World { get; }
    public IReadOnlyList<int> InitialItemIds { get; }
}
