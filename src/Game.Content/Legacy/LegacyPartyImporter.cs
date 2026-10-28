using System.Buffers.Binary;
using JinYong.Core.Session;

namespace JinYong.Content.Legacy;

/// <summary>RANGER sections 0/1/2: header, roles, item ownership. Original files remain read-only.</summary>
public static class LegacyPartyImporter
{
    // OpenLegend model/game_snapshot.hpp: 836-byte header, 182-byte role, 190-byte item.
    public static PartyDefinition Read(LegacyArchive ranger)
    {
        if (ranger.Count != 6) throw new InvalidDataException("RANGER requires six sections.");
        return Read(ranger.Entry(0), ranger.Entry(1), ranger.Entry(2));
    }
    public static PartyDefinition Read(ReadOnlySpan<byte> header, ReadOnlySpan<byte> roles, ReadOnlySpan<byte> items)
    {
        if (header.Length != 836 || roles.Length == 0 || roles.Length % 182 != 0 || items.Length % 190 != 0)
            throw new InvalidDataException("Invalid RANGER party record lengths.");
        var definitions = new List<RoleDefinition>();
        for (var i = 0; i < roles.Length / 182; i++)
        {
            var bytes = roles.Slice(i * 182, 182);
            var words = new int[RoleWords.Count];
            for (var word = 0; word < words.Length; word++) words[word] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(word * 2)..]);
            if (words[RoleWords.Id] != i) throw new InvalidDataException($"RANGER role {i} has an inconsistent ID.");
            definitions.Add(new(i, LegacyTextDecoder.DecodeName(bytes.Slice(8, 10)),
                LegacyTextDecoder.DecodeName(bytes.Slice(18, 10)), words));
        }
        var members = new int[PartyState.Capacity];
        for (var slot = 0; slot < members.Length; slot++)
        {
            var id = BinaryPrimitives.ReadInt16LittleEndian(header[((12 + slot) * 2)..]);
            members[slot] = slot > 0 && id == 0 ? -1 : id;
        }
        var itemUsers = new Dictionary<int, int>();
        for (var i = 0; i < items.Length / 190; i++) itemUsers.Add(i, BinaryPrimitives.ReadInt16LittleEndian(items[(i * 190 + 38 * 2)..]));
        try { return new(definitions, members, itemUsers); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid RANGER party references.", error); }
    }
}
