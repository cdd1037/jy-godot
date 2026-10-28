using System.Buffers.Binary;
using JinYong.Core.Battles;

namespace JinYong.Importing.Legacy;

public sealed record LegacyBattleData(IReadOnlyList<BattleDefinition> Battles,
    IReadOnlyList<BattlefieldDefinition> Fields, IReadOnlyList<MagicDefinition> Magics, IReadOnlyList<ItemDefinition> Items);

/// <summary>
/// Pure data import, no battle runtime, original executable or texture output.
/// Sources: OpenLegend battle/battle_data.cpp and model/game_snapshot.hpp.
/// </summary>
public static class LegacyBattleImporter
{
    public static LegacyBattleData Read(Func<string, byte[]> read, LegacyArchive ranger)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (ranger.Count != 6) throw new InvalidDataException("RANGER requires six sections.");
        var battles = ReadBattles(read("WAR.STA"));
        var fields = ReadFields(LegacyArchive.Parse(read("WARFLD.IDX"), read("WARFLD.GRP")));
        foreach (var battle in battles)
            if (battle.BattlefieldId < 0 || battle.BattlefieldId >= fields.Count)
                throw new InvalidDataException($"Battle {battle.Id} references missing battlefield {battle.BattlefieldId}.");
        return new(battles, fields, ReadMagics(ranger), ReadItems(ranger));
    }

    public static IReadOnlyList<BattleDefinition> ReadBattles(ReadOnlySpan<byte> bytes)
    {
        const int size = BattleWords.Count * 2;
        if (bytes.IsEmpty || bytes.Length % size != 0) throw new InvalidDataException("WAR.STA must contain complete 186-byte battle records.");
        var battles = new BattleDefinition[bytes.Length / size];
        for (var id = 0; id < battles.Length; id++)
        {
            var record = bytes.Slice(id * size, size);
            battles[id] = new(id, Words(record), LegacyTextDecoder.DecodeName(record.Slice(2, 10)));
        }
        return Array.AsReadOnly(battles);
    }

    public static IReadOnlyList<BattlefieldDefinition> ReadFields(LegacyArchive archive)
    {
        const int size = BattlefieldDefinition.LayerCount * BattlefieldDefinition.TileCount * 2;
        var fields = new BattlefieldDefinition[archive.Count];
        for (var id = 0; id < fields.Length; id++)
        {
            var bytes = archive.Entry(id);
            // Native BattleData reads the first two layers. Original WARFLD has
            // both 16384- and 24576-byte records; archive offsets, not a fixed
            // stride, locate the next field. The source vault preserves tails.
            if (bytes.Length < size) throw new InvalidDataException($"WARFLD {id} must contain at least 16384 bytes.");
            var words = new short[size / 2];
            for (var word = 0; word < words.Length; word++) words[word] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(word * 2)..]);
            fields[id] = new(id, words);
        }
        return Array.AsReadOnly(fields);
    }

    public static IReadOnlyList<MagicDefinition> ReadMagics(LegacyArchive ranger)
    {
        if (ranger.Count != 6) throw new InvalidDataException("RANGER requires six sections.");
        var bytes = ranger.Entry(4);
        const int size = MagicWords.Count * 2;
        if (bytes.IsEmpty || bytes.Length % size != 0) throw new InvalidDataException("RANGER magics must contain complete 136-byte records.");
        var magics = new MagicDefinition[bytes.Length / size];
        for (var id = 0; id < magics.Length; id++)
        {
            var record = bytes.Slice(id * size, size);
            try { magics[id] = new(id, LegacyTextDecoder.DecodeName(record.Slice(2, 10)), Words(record)); }
            catch (ArgumentException error) { throw new InvalidDataException($"Invalid RANGER magic {id}.", error); }
        }
        return Array.AsReadOnly(magics);
    }

    public static IReadOnlyList<ItemDefinition> ReadItems(LegacyArchive ranger)
    {
        if (ranger.Count != 6) throw new InvalidDataException("RANGER requires six sections.");
        var bytes = ranger.Entry(2);
        const int size = ItemWords.Count * 2;
        if (bytes.IsEmpty || bytes.Length % size != 0) throw new InvalidDataException("RANGER items must contain complete 190-byte records.");
        var items = new ItemDefinition[bytes.Length / size];
        for (var id = 0; id < items.Length; id++)
        {
            var record = bytes.Slice(id * size, size);
            try { items[id] = new(id, LegacyTextDecoder.DecodeName(record.Slice(2, 20)), Words(record)); }
            catch (ArgumentException error) { throw new InvalidDataException($"Invalid RANGER item {id}.", error); }
        }
        return Array.AsReadOnly(items);
    }

    private static int[] Words(ReadOnlySpan<byte> bytes)
    {
        var words = new int[bytes.Length / 2];
        for (var index = 0; index < words.Length; index++) words[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(index * 2)..]);
        return words;
    }
}
