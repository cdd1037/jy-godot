using System.Buffers.Binary;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;

namespace JinYong.Importing.Legacy;

/// <summary>
/// Imports one global script/dialogue/role catalog. Every KDEF ID is classified,
/// including computed opcode-26 targets; no scene closure or dependency profile.
/// Layout and opcode source: OpenLegend model/game_snapshot.hpp and scene/scene.cpp.
/// </summary>
public static class LegacyGameDataImporter
{
    public static GameDataDocument Read(Func<string, byte[]> read, LegacyArchive ranger,
        LegacyScriptDialect dialect = LegacyScriptDialect.DosOriginal)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (dialect != LegacyScriptDialect.DosOriginal)
            throw new NotSupportedException($"Global import does not support script profile {dialect}.");
        var scripts = LegacyArchive.Parse(read("KDEF.IDX"), read("KDEF.GRP"));
        var talks = LegacyArchive.Parse(read("TALK.IDX"), read("TALK.GRP"));
        var party = LegacyPartyImporter.Read(ranger);
        var itemBytes = ranger.Entry(2);
        if (itemBytes.Length % 190 != 0) throw new InvalidDataException("Invalid RANGER item record length.");
        var itemNames = new Dictionary<int, string>();
        for (var i = 0; i < itemBytes.Length / 190; i++)
            itemNames.Add(i, LegacyTextDecoder.DecodeName(itemBytes.Slice(i * 190 + 2, 20)));
        var header = ranger.Entry(0);
        var inventory = new Dictionary<int, int>();
        var itemIds = new HashSet<int>();
        for (var slot = 0; slot < 200; slot++)
        {
            var id = BinaryPrimitives.ReadInt16LittleEndian(header[(36 + slot * 4)..]);
            var count = BinaryPrimitives.ReadInt16LittleEndian(header[(38 + slot * 4)..]);
            if (id < 0) continue;
            if (!itemNames.ContainsKey(id)) throw new InvalidDataException($"Inventory slot {slot} references unknown item {id}.");
            itemIds.Add(id);
            if (count > 0) inventory[id] = checked(inventory.GetValueOrDefault(id) + count);
        }

        var programs = new Dictionary<int, EventProgramDocument>();
        var unavailable = new Dictionary<int, string>();
        for (var id = 0; id < scripts.Count; id++)
        {
            try { programs.Add(id, LegacyEventCompiler.Compile(id, scripts.Entry(id), dialect)); }
            catch (Exception error) when (IsProgramError(error)) { unavailable.Add(id, error.Message); }
        }
        var decodedTalks = new Dictionary<int, DialogueDocument>();
        var badTalks = new Dictionary<int, string>();
        DialogueDocument Dialogue(int id)
        {
            if (decodedTalks.TryGetValue(id, out var known)) return known;
            if (badTalks.TryGetValue(id, out var bad)) throw new InvalidDataException(bad);
            try
            {
                var dialogue = new DialogueDocument(id, LegacyTextDecoder.DecodeDialogue(talks.Entry(id)));
                decodedTalks.Add(id, dialogue);
                return dialogue;
            }
            catch (Exception error) when (IsProgramError(error))
            {
                var reason = $"TALK {id}: {error.Message}";
                badTalks.Add(id, reason);
                throw new InvalidDataException(reason, error);
            }
        }
        // Each original program is validated with all archive IDs known. A
        // replacement may refer to an explicitly unavailable program, while an
        // out-of-archive ID is a malformed reference and rejects its source.
        var knownReferences = Enumerable.Range(0, scripts.Count).ToDictionary(id => id, _ => "Known global KDEF entry.");
        foreach (var document in programs.Values.ToArray())
        {
            try
            {
                var localDialogues = DialogueIds(document).Distinct().Select(Dialogue).ToArray();
                var program = new EventProgram(document.Id, document.Commands.Select(command =>
                    new EventCommand(command.Id, command.Operation, command.Arguments, command.SourceOpcode, command.Text, command.Choices)));
                _ = new StoryCatalog(0, null, -1, null, Direction.Down, [program],
                    localDialogues.Select(dialogue => new DialogueDefinition(dialogue.Id, dialogue.Pages)), itemNames, inventory,
                    knownReferences, party: party, initialItemIds: itemIds);
            }
            catch (Exception error) when (IsProgramError(error))
            {
                programs.Remove(document.Id);
                unavailable.Add(document.Id, $"KDEF {document.Id}: {error.Message}");
            }
        }
        var names = new Dictionary<int, string> { [114] = "軟體娃娃" };
        foreach (var role in party.Roles)
        {
            var head = role.Words[1];
            if (head >= 0) names.TryAdd(head, role.Name);
        }
        var data = new GameDataDocument
        {
            Programs = programs.Values.OrderBy(program => program.Id).ToArray(),
            Dialogues = programs.Values.SelectMany(DialogueIds).Distinct().Order().Select(Dialogue).ToArray(),
            ItemNames = itemNames, InitialInventory = inventory, InitialItemIds = itemIds.Order().ToArray(),
            Party = party, UnavailablePrograms = unavailable, PortraitNames = names
        };
        _ = data.ToStoryCatalog(0); // Verify final global references together, not merely individual decoding.
        return data;
    }

    private static IEnumerable<int> DialogueIds(EventProgramDocument program)
    {
        foreach (var command in program.Commands)
        {
            if (command.Operation == EventOperation.Dialogue) yield return command.Arguments[0];
            else if (command.Operation == EventOperation.RandomDialogue)
                for (var offset = 0; offset < command.Arguments[1]; offset++) yield return checked(command.Arguments[0] + offset);
        }
    }
    private static bool IsProgramError(Exception error) => error is NotSupportedException or InvalidDataException or
        ArgumentException or OverflowException;
}
