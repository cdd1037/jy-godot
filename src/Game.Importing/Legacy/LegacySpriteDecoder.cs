using System.Buffers.Binary;
using JinYong.Content.Imaging;

namespace JinYong.Importing.Legacy;

public sealed record IndexedSprite(int Width, int Height, int OffsetX, int OffsetY, byte[] Indices, byte[] Alpha);

public static class LegacySpriteDecoder
{
    public static IndexedSprite Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return new(0, 0, 0, 0, [], []);
        if (bytes.Length < 8) throw new InvalidDataException("Sprite header is truncated.");
        var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]);
        var offsetX = BinaryPrimitives.ReadInt16LittleEndian(bytes[4..]);
        var offsetY = BinaryPrimitives.ReadInt16LittleEndian(bytes[6..]);
        if (width > 2048 || height > 2048)
            throw new InvalidDataException($"Invalid sprite dimensions {width}x{height}.");
        var indices = new byte[width * height];
        var alpha = new byte[indices.Length];
        var cursor = 8;
        for (var y = 0; y < height; y++)
        {
            if (cursor >= bytes.Length) throw new InvalidDataException($"Missing row length at row {y}.");
            var end = cursor + 1 + bytes[cursor];
            cursor++;
            if (end > bytes.Length) throw new InvalidDataException($"Sprite row {y} exceeds frame.");
            var x = 0;
            while (cursor < end)
            {
                if (end - cursor < 2) throw new InvalidDataException("Truncated RLE run.");
                x += bytes[cursor++];
                var count = bytes[cursor++];
                if (cursor + count > end || x + count > width) throw new InvalidDataException("RLE run exceeds row or width.");
                bytes.Slice(cursor, count).CopyTo(indices.AsSpan(y * width + x, count));
                alpha.AsSpan(y * width + x, count).Fill(255);
                cursor += count;
                x += count;
            }
        }
        if (cursor != bytes.Length) throw new InvalidDataException("Sprite frame contains trailing bytes.");
        if (width == 0 || height == 0) return new(0, 0, offsetX, offsetY, [], []);
        return new(width, height, offsetX, offsetY, indices, alpha);
    }

    public static byte[] ReadPalette(ReadOnlySpan<byte> palette) => VgaPalette.DecodeRgb6(palette);
}
