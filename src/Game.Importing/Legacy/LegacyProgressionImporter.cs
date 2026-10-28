using System.Buffers.Binary;
using JinYong.Core.Battles;

namespace JinYong.Importing.Legacy;

/// <summary>
/// Extracts data from Z.DAT, never stores or runs the DOS executable.
/// Source: OpenLegend battle/battle_data.cpp load_progression_data.
/// Instruction-layout guards prevent silently accepting offsets in another binary.
/// </summary>
public static class LegacyProgressionImporter
{
    public const int ExperienceTableOffset = 0x4DF8E;
    public static ProgressionDefinition Read(Func<string, byte[]> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Read(read("Z.DAT"));
    }

    public static ProgressionDefinition Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ExperienceTableOffset + ProgressionDefinition.OriginalLevelCount * 2)
            throw new InvalidDataException("Z.DAT is shorter than the original progression data.");
        static void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException(message); }
        Require(bytes.Slice(0x250EF, 3).SequenceEqual(new byte[] { 0x66, 0x81, 0xB8 }) &&
            U32(bytes, 0x250F2) == 0x70170 && bytes.Slice(0x250FA, 3).SequenceEqual(new byte[] { 0x66, 0xC7, 0x80 }) &&
            U32(bytes, 0x250FD) == 0x70170 && I16(bytes, 0x250F6) == I16(bytes, 0x25101),
            "Z.DAT maximum HP instruction layout does not match the DOS baseline.");
        Require(bytes.Slice(0x253B7, 3).SequenceEqual(new byte[] { 0x66, 0x81, 0xB8 }) &&
            U32(bytes, 0x253BA) == 0x701A0 && bytes.Slice(0x253C2, 3).SequenceEqual(new byte[] { 0x66, 0xC7, 0x80 }) &&
            U32(bytes, 0x253C5) == 0x701A0 && I16(bytes, 0x253BE) == I16(bytes, 0x253C9),
            "Z.DAT maximum MP instruction layout does not match the DOS baseline.");
        Require(bytes[0x35521] == 0xBB && bytes[0x35531] == 0xBA && bytes[0x35556] == 0xBB && bytes[0x35566] == 0xBA &&
            bytes[0x3553B] == 0x8D && bytes[0x3553C] == 0x56 && bytes[0x35570] == 0x01 && bytes[0x35571] == 0xD2 &&
            bytes[0x34F1A] == 0xC1 && bytes[0x34F1B] == 0xE0 && bytes[0x34F1D] == 0xBD && bytes[0x34F1C] < 31 &&
            U32(bytes, 0x35522) == U32(bytes, 0x35557) && U32(bytes, 0x35532) == U32(bytes, 0x35567),
            "Z.DAT practice instruction layout does not match the DOS baseline.");
        var thresholds = new int[ProgressionDefinition.OriginalLevelCount];
        for (var level = 0; level < thresholds.Length; level++)
            thresholds[level] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(ExperienceTableOffset + level * 2)..]);
        try
        {
            return new(thresholds, I16(bytes, 0x250F6), I16(bytes, 0x253BE),
                I32(bytes, 0x35532), I32(bytes, 0x35522), unchecked((sbyte)bytes[0x3553D]),
                2, 1 << bytes[0x34F1C], I32(bytes, 0x34F1E));
        }
        catch (ArgumentException error) { throw new InvalidDataException("Z.DAT contains invalid original progression rule parameters.", error); }
    }

    private static short I16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    private static int I32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
