namespace JinYong.Content.Ending;

/// <summary>Normalized ending artwork. DOS archive/RLE decoding belongs to Importing.</summary>
public sealed class EndingAssets
{
    public const int Width = 320, Height = 200, PixelCount = Width * Height;
    public const int RequiredWords = 23, RequiredFrames = 221;
    private readonly byte[] _pixels, _palette;
    private readonly EndingWordAsset[] _words;
    private readonly int[] _frames;

    internal EndingAssets(EndingAssetsDocument document, byte[] pixels)
    {
        _pixels = pixels;
        _palette = document.PaletteRgb.ToArray();
        _words = document.Words.ToArray();
        _frames = document.FrameOffsets.ToArray();
        Words = Array.AsReadOnly(_words);
    }

    public IReadOnlyList<EndingWordAsset> Words { get; }
    public int FrameCount => _frames.Length;

    public byte[] WordRgba(int index)
    {
        var word = _words[index];
        return Rgba(word.PixelsOffset, word.Width * word.Height, word.AlphaOffset);
    }

    public byte[] FrameRgba(int index) => Rgba(_frames[index], PixelCount, null);

    private byte[] Rgba(int offset, int count, int? alphaOffset)
    {
        var rgba = new byte[count * 4];
        for (var pixel = 0; pixel < count; pixel++)
        {
            var color = _pixels[offset + pixel] * 3;
            rgba[pixel * 4] = _palette[color];
            rgba[pixel * 4 + 1] = _palette[color + 1];
            rgba[pixel * 4 + 2] = _palette[color + 2];
            rgba[pixel * 4 + 3] = alphaOffset is { } alpha ? _pixels[alpha + pixel] : (byte)255;
        }
        return rgba;
    }
}

public sealed record EndingWordAsset(int Width, int Height, int OffsetX, int OffsetY, int PixelsOffset, int AlphaOffset);

/// <summary>Portable normalized payload produced by LegacyEndingImporter and tracked by the library manifest.</summary>
public sealed record EndingAssetsDocument
{
    public const string DocumentPath = "data/ending.json", PixelsPath = "data/ending.pixels.bin";
    public int FormatVersion { get; init; } = 1;
    public byte[] PaletteRgb { get; init; } = [];
    public EndingWordAsset[] Words { get; init; } = [];
    public int[] FrameOffsets { get; init; } = [];
}
