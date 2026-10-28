using System.Text;

namespace JinYong.Content.Legacy;

public static class LegacyTextDecoder
{
    public static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var zero = bytes.IndexOf((byte)0);
        return Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            .GetString(zero >= 0 ? bytes[..zero] : bytes).TrimEnd();
    }

    // Decode XOR-FF before treating '*' as a line separator. Every third line
    // break commits a page, including intentional blank lines in the original.
    public static string[] DecodeDialogue(ReadOnlySpan<byte> encoded)
    {
        var decoded = new List<byte>();
        foreach (var value in encoded)
        {
            if (value == 0 || (value ^ 0xFF) == 0) break;
            decoded.Add((byte)(value ^ 0xFF));
        }
        var text = DecodeName(decoded.ToArray());
        var pages = new List<string>();
        var page = new StringBuilder();
        var breaks = 0;
        foreach (var character in text)
        {
            page.Append(character == '*' ? '\n' : character);
            if (character == '*' && ++breaks == 3)
            {
                pages.Add(page.ToString().TrimEnd('\n'));
                page.Clear(); breaks = 0;
            }
        }
        pages.Add(page.ToString().TrimEnd('\n'));
        return pages.ToArray();
    }
}
