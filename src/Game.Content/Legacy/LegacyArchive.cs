using System.Buffers.Binary;

namespace JinYong.Content.Legacy;

public sealed class LegacyArchive
{
    private readonly byte[] _data;
    private readonly (int Start, int End)[] _ranges;
    private LegacyArchive(byte[] data, (int, int)[] ranges) { _data = data; _ranges = ranges; }
    public int Count => _ranges.Length;

    // IDX entries are cumulative ends; SDX has a final zero sentinel and its last
    // frame extends to EOF. Zero-width SDX entries alias the next nonempty frame.
    public static LegacyArchive Parse(byte[] index, byte[] data, bool sentinel = false)
    {
        if (index.Length == 0 || index.Length % 4 != 0) throw new InvalidDataException("IDX length must be a nonzero multiple of four.");
        var offsets = new uint[index.Length / 4];
        for (var i = 0; i < offsets.Length; i++) offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 4));
        var ranges = new List<(int, int)>();
        var start = 0;
        if (sentinel && offsets[^1] != 0) offsets = [.. offsets, 0];
        var count = sentinel ? offsets.Length - 1 : offsets.Length;
        var zeroTail = false;
        for (var i = 0; i < count; i++)
        {
            var end = offsets[i];
            if (!sentinel && end == 0 && start > 0) { zeroTail = true; continue; }
            if (zeroTail || end < start || end > data.Length) throw new InvalidDataException($"Invalid archive offset at entry {i}.");
            ranges.Add((start, (int)end));
            start = (int)end;
        }
        if (sentinel) ranges.Add((start, data.Length));
        else if (start != data.Length) throw new InvalidDataException("Final IDX offset does not cover GRP data.");
        return new LegacyArchive(data, ranges.ToArray());
    }

    public ReadOnlySpan<byte> Entry(int index, bool aliasEmpty = false)
    {
        if (index < 0 || index >= Count) throw new InvalidDataException($"Archive entry {index} is outside 0..{Count - 1}.");
        var (start, end) = _ranges[index];
        while (aliasEmpty && start == end && ++index < Count) end = _ranges[index].End;
        return _data.AsSpan(start, end - start);
    }
}
