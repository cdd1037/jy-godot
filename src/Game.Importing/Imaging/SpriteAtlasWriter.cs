using JinYong.Content.Packages;
using JinYong.Importing.Legacy;

namespace JinYong.Importing.Imaging;

internal static class SpriteAtlasWriter
{
    private const int Size = 1024;

    public static SpriteAsset[] Write(string output, IReadOnlyDictionary<int, IndexedSprite> sprites, byte[] palette, string folder = "textures")
    {
        Directory.CreateDirectory(Path.Combine(output, folder));
        var assets = new List<SpriteAsset>();
        var indices = new byte[Size * Size];
        var alpha = new byte[indices.Length];
        var page = 0;
        var x = 0;
        var y = 0;
        var rowHeight = 0;
        var usedHeight = 0;
        void Flush()
        {
            if (usedHeight == 0) return;
            var pixels = new byte[Size * usedHeight * 4];
            for (var i = 0; i < Size * usedHeight; i++)
            {
                pixels[i * 4] = palette[indices[i] * 3];
                pixels[i * 4 + 1] = palette[indices[i] * 3 + 1];
                pixels[i * 4 + 2] = palette[indices[i] * 3 + 2];
                pixels[i * 4 + 3] = alpha[i];
            }
            var stem = Path.Combine(output, folder, $"atlas-{page:D3}");
            PngEncoder.Write(stem + ".png", Size, usedHeight, pixels);
            File.WriteAllBytes(stem + ".indices", indices[..(Size * usedHeight)]);
            File.WriteAllBytes(stem + ".alpha", alpha[..(Size * usedHeight)]);
        }

        foreach (var (id, sprite) in sprites.OrderBy(pair => pair.Key))
        {
            if (sprite.Width == 0)
            {
                assets.Add(new SpriteAsset { LegacyId = id, OffsetX = sprite.OffsetX, OffsetY = sprite.OffsetY });
                continue;
            }
            if (sprite.Width > Size || sprite.Height > Size) throw new InvalidDataException($"Sprite {id} exceeds atlas page size.");
            if (x + sprite.Width > Size) { x = 0; y += rowHeight + 1; rowHeight = 0; }
            if (y + sprite.Height > Size)
            {
                Flush();
                page++;
                Array.Clear(indices);
                Array.Clear(alpha);
                x = y = rowHeight = usedHeight = 0;
            }
            for (var row = 0; row < sprite.Height; row++)
            {
                sprite.Indices.AsSpan(row * sprite.Width, sprite.Width).CopyTo(indices.AsSpan((y + row) * Size + x));
                sprite.Alpha.AsSpan(row * sprite.Width, sprite.Width).CopyTo(alpha.AsSpan((y + row) * Size + x));
            }
            assets.Add(new SpriteAsset
            {
                LegacyId = id, Atlas = $"{folder}/atlas-{page:D3}.png", X = x, Y = y,
                Width = sprite.Width, Height = sprite.Height, OffsetX = sprite.OffsetX, OffsetY = sprite.OffsetY
            });
            x += sprite.Width + 1;
            rowHeight = Math.Max(rowHeight, sprite.Height);
            usedHeight = Math.Max(usedHeight, y + sprite.Height);
        }
        Flush();
        return assets.ToArray();
    }
}
