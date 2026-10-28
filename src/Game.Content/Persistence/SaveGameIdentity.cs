using System.Security.Cryptography;
using System.Text.Json;
using JinYong.Content.Library;
using JinYong.Content.Packages;

namespace JinYong.Content.Persistence;

public static class SaveGameIdentity
{
    /// <summary>Verifies actual tracked files before hashing portable manifest content. No directory name participates.</summary>
    public static string ForLibrary(GameLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return Library(library.Manifest, path =>
        {
            using var stream = File.OpenRead(library.Resolve(path));
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        });
    }

    // Pure content adapter for import tools and independently authored fixtures.
    public static string ForLibrary(GameLibraryManifest manifest, Func<string, byte[]> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Library(manifest, path => Convert.ToHexStringLower(SHA256.HashData(read(path))));
    }

    private static string Library(GameLibraryManifest manifest, Func<string, string> actualHash)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FormatVersion != 3 || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.EngineApi) ||
            manifest.FileHashes.Count == 0) throw new InvalidDataException("Invalid library identity manifest.");
        foreach (var (path, expected) in manifest.FileHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            RequireRelativePath(path);
            if (!IsHash(expected) || !expected.Equals(actualHash(path), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Library identity checksum mismatch: {path}");
        }
        foreach (var (name, hash) in manifest.SourceHashes)
            if (Path.GetFileName(name) != name || string.IsNullOrWhiteSpace(name) || !IsHash(hash))
                throw new InvalidDataException("Invalid library source identity.");
        // Include every manifest binding/version as well as IDs, engine API and
        // both hash dictionaries; changing a binding must not keep an old save ID.
        var normalized = manifest with
        {
            FileHashes = manifest.FileHashes.ToDictionary(pair => pair.Key, pair => pair.Value.ToLowerInvariant()),
            SourceHashes = manifest.SourceHashes.ToDictionary(pair => pair.Key, pair => pair.Value.ToLowerInvariant())
        };
        return Hash(normalized);
    }

    public static string ForStory(StoryDocument? overlay) => Hash(overlay);
    internal static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(value)));
    internal static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);
    internal static void RequireRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Split('/', '\\').Any(part => part is "." or ".."))
            throw new InvalidDataException("Library identity paths must stay relative to the resource library.");
    }

    internal static byte[] CanonicalBytes<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, ContentJson.Options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        return stream.ToArray();
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default: value.WriteTo(writer); break;
        }
    }
}
