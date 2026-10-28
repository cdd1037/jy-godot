namespace JinYong.Content.Imaging;

public static class VgaPalette
{
    public static byte[] DecodeRgb6(ReadOnlySpan<byte> palette)
    {
        if (palette.Length != 768) throw new InvalidDataException("MMAP.COL must contain 768 bytes.");
        var rgb = new byte[768];
        for (var i = 0; i < palette.Length; i++)
        {
            var channel = palette[i];
            if (channel > 63) throw new InvalidDataException("Palette channel exceeds VGA six-bit range.");
            rgb[i] = (byte)((channel << 2) | (channel >> 4));
        }
        return rgb;
    }
}
