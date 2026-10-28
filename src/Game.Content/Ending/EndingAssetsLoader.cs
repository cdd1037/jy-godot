using System.Security.Cryptography;
using JinYong.Content.Library;
using JinYong.Content.Packages;

namespace JinYong.Content.Ending;

public static class EndingAssetsLoader
{
    public static EndingAssets Load(GameLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        byte[] Read(string relative)
        {
            if (!library.Manifest.FileHashes.TryGetValue(relative, out var hash))
                throw new InvalidDataException("资源库缺少完整结局资料，请重新导入包含 ENDCOL、ENDWORD 和 KEND 的原版资源。");
            var bytes = File.ReadAllBytes(library.Resolve(relative));
            if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"结局资源校验失败：{relative}");
            return bytes;
        }
        var documentBytes = Read(EndingAssetsDocument.DocumentPath);
        var document = System.Text.Json.JsonSerializer.Deserialize<EndingAssetsDocument>(documentBytes, ContentJson.Options)
            ?? throw new InvalidDataException("结局资源描述为空。");
        var pixels = Read(EndingAssetsDocument.PixelsPath);
        if (document.FormatVersion != 1 || document.PaletteRgb is not { Length: 768 } ||
            document.Words is not { Length: EndingAssets.RequiredWords } || document.FrameOffsets is not { Length: EndingAssets.RequiredFrames })
            throw new InvalidDataException("结局资料必须包含调色板、23 张字图和 221 帧动画。");
        // Require the contiguous normalized layout. Overlaps/trailing bytes cannot hide malformed frames.
        long cursor = 0;
        foreach (var word in document.Words)
        {
            if (word is null || word.Width is <= 0 or > 2048 || word.Height is <= 0 or > 2048 ||
                word.OffsetX is < short.MinValue or > short.MaxValue || word.OffsetY is < short.MinValue or > short.MaxValue)
                throw new InvalidDataException("结局字图的尺寸或偏移无效。");
            var count = (long)word.Width * word.Height;
            if (word.PixelsOffset != cursor || word.AlphaOffset != cursor + count || cursor + 2 * count > pixels.Length)
                throw new InvalidDataException("结局字图数据越界。");
            foreach (var alpha in pixels.AsSpan(word.AlphaOffset, (int)count))
                if (alpha is not (0 or 255)) throw new InvalidDataException("结局字图透明度无效。");
            cursor += 2 * count;
        }
        foreach (var offset in document.FrameOffsets)
        {
            if (offset != cursor || cursor + EndingAssets.PixelCount > pixels.Length)
                throw new InvalidDataException("结局动画必须逐帧包含 320×200 个像素。");
            cursor += EndingAssets.PixelCount;
        }
        if (cursor != pixels.Length) throw new InvalidDataException("结局像素数据包含多余内容。");
        return new(document, pixels);
    }
}
