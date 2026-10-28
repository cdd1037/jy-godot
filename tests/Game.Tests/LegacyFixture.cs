using System.Buffers.Binary;
using System.Text;
using JinYong.Core.Maps;

namespace JinYong.Tests;

// Independently authored format fixture. This is NOT original game content.
internal static class LegacyFixture
{
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JinYong.slnx"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Cannot locate test workspace.");
        }
    }

    public static (string Source, string Output) Create()
    {
        var root = Path.Combine(Root, ".local", "tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        void Pair(string name, byte[][] entries)
        {
            using var group = new MemoryStream();
            var ends = new List<uint>();
            foreach (var entry in entries) { group.Write(entry); ends.Add((uint)group.Length); }
            File.WriteAllBytes(Path.Combine(source, name + ".IDX"), LegacyFormatTests.Index(ends.ToArray()));
            File.WriteAllBytes(Path.Combine(source, name + ".GRP"), group.ToArray());
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var metadata = new byte[71 * 52];
        var name = Encoding.GetEncoding(950).GetBytes("格式測試場");
        for (var i = 0; i < 71; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(metadata.AsSpan(i * 52), (short)i);
            name.CopyTo(metadata, i * 52 + 2);
            BinaryPrimitives.WriteInt16LittleEndian(metadata.AsSpan(i * 52 + 28), 19);
            BinaryPrimitives.WriteInt16LittleEndian(metadata.AsSpan(i * 52 + 30), 20);
        }
        Pair("RANGER", [new byte[836], new byte[182], new byte[190], metadata, new byte[136], new byte[30]]);
        var layers = MovementTests.EmptyLayers();
        void Set(MapLayer layer, int x, int y, short value) => layers[(int)layer * 4096 + y * 64 + x] = value;
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
        {
            Set(MapLayer.Ground, x, y, (short)(((x + y) % 3 == 0) ? 2 : 0));
            if (((x == 11 || x == 29) && y is >= 11 and <= 29) || ((y == 11 || y == 29) && x is >= 11 and <= 29))
                Set(MapLayer.Building, x, y, 4);
            if (x is >= 23 and <= 26 && y is >= 19 and <= 22) Set(MapLayer.Ground, x, y, 358);
        }
        Set(MapLayer.Event, 20, 20, 0);
        Set(MapLayer.Decoration, 15, 18, 8);
        Set(MapLayer.Building, 15, 18, 15000);
        var layerBytes = new byte[layers.Length * 2];
        for (var i = 0; i < layers.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(layerBytes.AsSpan(i * 2), layers[i]);
        var events = new byte[4400];
        BinaryPrimitives.WriteInt16LittleEndian(events, 1);
        BinaryPrimitives.WriteInt16LittleEndian(events.AsSpan(5 * 2), 6);
        BinaryPrimitives.WriteInt16LittleEndian(events.AsSpan(7 * 2), 6);
        BinaryPrimitives.WriteInt16LittleEndian(events.AsSpan(9 * 2), 20);
        BinaryPrimitives.WriteInt16LittleEndian(events.AsSpan(10 * 2), 20);
        Pair("ALLSIN", Enumerable.Repeat(layerBytes, 71).ToArray());
        Pair("ALLDEF", Enumerable.Repeat(events, 71).ToArray());
        var palette = new byte[768];
        void Color(int index, byte red, byte green, byte blue) { palette[index * 3] = red; palette[index * 3 + 1] = green; palette[index * 3 + 2] = blue; }
        Color(10, 25, 36, 23); Color(11, 30, 41, 27); Color(12, 23, 30, 20);
        Color(20, 43, 38, 26); Color(21, 32, 28, 19); Color(22, 50, 45, 33);
        Color(30, 13, 28, 37); Color(31, 18, 35, 43);
        Color(40, 43, 19, 15); Color(41, 57, 44, 30); Color(42, 11, 13, 12);
        Color(45, 15, 27, 18); Color(46, 22, 36, 22);
        File.WriteAllBytes(Path.Combine(source, "MMAP.COL"), palette);
        var frames = new Dictionary<int, byte[]>();
        frames[0] = Diamond(10); frames[1] = Diamond(11); frames[179] = Diamond(30);
        frames[2] = Sprite(36, 52, 18, 43, (x, y) =>
        {
            if (y >= 43) return Math.Abs(x - 17) <= (51 - y) * 2 ? 21 : -1;
            if (y < 9) return Math.Abs(x - 17) <= y * 2 ? 22 : -1;
            return x is >= 1 and <= 34 ? ((y % 8 == 0 || (x + (y / 8 % 2) * 8) % 16 == 0) ? 21 : 20) : -1;
        });
        frames[4] = Sprite(44, 64, 22, 55, (x, y) =>
        {
            if (y > 44 && x is >= 19 and <= 23) return 21;
            return Math.Pow((x - 21) / 21.0, 2) + Math.Pow((y - 25) / 25.0, 2) < 1 ? (x % 7 == 0 ? 45 : 46) : -1;
        });
        byte[] Actor(int frame) => Sprite(22, 40, 11, 36, (x, y) =>
        {
            if (y is >= 3 and <= 10 && x is >= 7 and <= 14) return y < 6 ? 42 : 41;
            if (y is >= 11 and <= 28 && Math.Abs(x - 10) <= (y < 20 ? 6 : 8)) return 40;
            var offset = frame % 3 - 1;
            if (y is >= 29 and <= 36 && (Math.Abs(x - 7 - offset) < 2 || Math.Abs(x - 13 + offset) < 2)) return 42;
            return -1;
        });
        frames[3] = Actor(0);
        for (var i = 2501; i <= 2528; i++) frames[i] = Actor(i);
        using var spriteGroup = new MemoryStream();
        var spriteEnds = new List<uint>();
        for (var i = 0; i <= 2528; i++)
        {
            if (frames.TryGetValue(i, out var frame)) spriteGroup.Write(frame);
            if (i < 2528) spriteEnds.Add((uint)spriteGroup.Length);
        }
        spriteEnds.Add(0);
        File.WriteAllBytes(Path.Combine(source, "SDX070"), LegacyFormatTests.Index(spriteEnds.ToArray()));
        File.WriteAllBytes(Path.Combine(source, "SMP070"), spriteGroup.ToArray());
        return (source, Path.Combine(root, "package"));
    }

    private static byte[] Diamond(int color) => Sprite(36, 18, 18, 9,
        (x, y) => Math.Abs(x - 17.5) / 18 + Math.Abs(y - 8.5) / 9 <= 1 ? color : -1);

    private static byte[] Sprite(int width, int height, short offsetX, short offsetY, Func<int, int, int> pixel)
    {
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)width); writer.Write((ushort)height); writer.Write(offsetX); writer.Write(offsetY);
        for (var y = 0; y < height; y++)
        {
            using var row = new MemoryStream();
            var x = 0;
            while (x < width)
            {
                var skip = 0;
                while (x < width && pixel(x, y) < 0) { skip++; x++; }
                if (x == width) break;
                var pixels = new List<byte>();
                while (x < width && pixel(x, y) >= 0) { pixels.Add((byte)pixel(x, y)); x++; }
                row.WriteByte((byte)skip); row.WriteByte((byte)pixels.Count); row.Write(pixels.ToArray());
            }
            writer.Write((byte)row.Length); writer.Write(row.ToArray());
        }
        return result.ToArray();
    }
}
