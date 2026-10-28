using JinYong.Content.Ending;
using JinYong.Content.Packages;

namespace JinYong.Importing.Legacy;

/// <summary>
/// Offline ending normalization from the central original-resource vault.
/// OpenLegend Z_DAT/0x30C3D.md: ENDCOL palette, ENDWORD entries 0..22
/// (even legacy IDs 0..44), KEND entries 0..220 of exactly 64000 raw pixels.
/// </summary>
public static class LegacyEndingImporter
{
    public static IReadOnlyList<string> RequiredFiles { get; } = Array.AsReadOnly(new[]
        { "ENDCOL.COL", "ENDWORD.IDX", "ENDWORD.GRP", "KEND.IDX", "KEND.GRP" });

    public static void Import(Func<string, byte[]> read, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(read);
        var palette = LegacySpriteDecoder.ReadPalette(read("ENDCOL.COL"));
        var words = LegacyArchive.Parse(read("ENDWORD.IDX"), read("ENDWORD.GRP"));
        var frames = LegacyArchive.Parse(read("KEND.IDX"), read("KEND.GRP"));
        if (words.Count < EndingAssets.RequiredWords || frames.Count < EndingAssets.RequiredFrames)
            throw new InvalidDataException("Ending resources need at least 23 ENDWORD images and 221 KEND frames.");
        using var pixels = new MemoryStream();
        var wordAssets = new EndingWordAsset[EndingAssets.RequiredWords];
        for (var index = 0; index < wordAssets.Length; index++)
        {
            var image = LegacySpriteDecoder.Decode(words.Entry(index));
            if (image.Width <= 0 || image.Height <= 0) throw new InvalidDataException($"ENDWORD {index} is empty.");
            var offset = checked((int)pixels.Position);
            pixels.Write(image.Indices);
            var alphaOffset = checked((int)pixels.Position);
            pixels.Write(image.Alpha);
            wordAssets[index] = new(image.Width, image.Height, image.OffsetX, image.OffsetY, offset, alphaOffset);
        }
        var offsets = new int[EndingAssets.RequiredFrames];
        for (var frame = 0; frame < offsets.Length; frame++)
        {
            var bytes = frames.Entry(frame);
            if (bytes.Length != EndingAssets.PixelCount) throw new InvalidDataException($"KEND {frame} must contain exactly 64000 pixels.");
            offsets[frame] = checked((int)pixels.Position);
            pixels.Write(bytes);
        }
        var documentPath = Path.Combine(outputDirectory, EndingAssetsDocument.DocumentPath);
        Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
        File.WriteAllBytes(Path.Combine(outputDirectory, EndingAssetsDocument.PixelsPath), pixels.ToArray());
        ContentJson.Write(documentPath, new EndingAssetsDocument { PaletteRgb = palette, Words = wordAssets, FrameOffsets = offsets });
    }
}
