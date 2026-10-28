using System.Text.Json;
using JinYong.Content.Packages;
using JinYong.Core.Persistence;
using JinYong.Core.Maps;

namespace JinYong.Content.Persistence;

/// <summary>
/// Three standalone remake slots, unrelated to DOS RANGER/S/D saves. Checksums
/// detect corruption, not adversarial authorship. Core Restore validates state.
/// </summary>
public sealed class SaveGameStore
{
    // Full visited-scene arrays can exceed 16 MiB in a legitimate late game.
    public const int MaximumSaveBytes = 64 * 1024 * 1024;
    private readonly string _directory;
    private readonly string[] _protectedDirectories;
    private readonly object _gate = new();
    private sealed record Envelope(int FormatVersion, string Checksum, SaveGameDocument Document);
    private static JsonSerializerOptions JsonOptions => new(ContentJson.Options) { WriteIndented = false, MaxDepth = 64 };

    public SaveGameStore(string directory, params string[] protectedDirectories)
    {
        _directory = Path.GetFullPath(directory);
        _protectedDirectories = protectedDirectories.Select(Path.GetFullPath).ToArray();
        ValidateDirectory();
    }
    public string Directory => _directory;
    public string GetSlotPath(int slot)
    {
        if (slot is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(slot), "Save slot must be 1, 2 or 3.");
        return Path.Combine(_directory, $"slot-{slot}.json");
    }

    public void Save(int slot, SaveGameDocument document)
    {
        var target = GetSlotPath(slot);
        ValidateDocument(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, SaveGameIdentity.Hash(document), document), JsonOptions);
        if (bytes.Length > MaximumSaveBytes) throw new InvalidDataException("Save exceeds the maximum file size.");
        lock (_gate)
        {
            ValidateDirectory();
            System.IO.Directory.CreateDirectory(_directory);
            var temporary = Path.Combine(_directory, $".slot-{slot}-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                { file.Write(bytes); file.Flush(flushToDisk: true); }
                if (File.Exists(target)) File.Replace(temporary, target, Path.Combine(_directory, $"slot-{slot}.backup.json"), ignoreMetadataErrors: true);
                else File.Move(temporary, target);
            }
            finally
            {
                if (File.Exists(temporary))
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    public SaveGameDocument Read(int slot)
    {
        var path = GetSlotPath(slot);
        lock (_gate)
        {
            ValidateDirectory();
            if (System.IO.Directory.Exists(path)) throw new InvalidDataException("Save slot path is not a file.");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > MaximumSaveBytes) throw new InvalidDataException("Save has an invalid file size.");
            var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
            if (file.ReadByte() != -1) throw new InvalidDataException("Save changed while it was being read.");
            try
            {
                using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
                RejectDuplicates(json.RootElement);
                RequireFields(json.RootElement, "format_version", "checksum", "document");
                var payload = json.RootElement.GetProperty("document");
                RequireFields(payload, "format_version", "snapshot", "library_fingerprint", "story_fingerprint", "utc_saved_at", "display_name");
                RequireSnapshotSchema(payload.GetProperty("snapshot"));
                var envelope = JsonSerializer.Deserialize<Envelope>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty save envelope.");
                if (envelope.FormatVersion != 1 || !SaveGameIdentity.IsHash(envelope.Checksum)) throw new InvalidDataException("Unsupported save envelope.");
                ValidateDocument(envelope.Document);
                if (!envelope.Checksum.Equals(SaveGameIdentity.Hash(envelope.Document), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Save checksum mismatch.");
                return envelope.Document;
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            { throw new InvalidDataException("Invalid save JSON or schema.", error); }
        }
    }

    public IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        var slots = new List<SaveSlotInfo>();
        for (var slot = 1; slot <= 3; slot++)
        {
            var path = GetSlotPath(slot);
            if (!File.Exists(path) && !System.IO.Directory.Exists(path)) { slots.Add(new(slot, SaveSlotStatus.Empty, "空存档")); continue; }
            try
            {
                var document = Read(slot);
                slots.Add(new(slot, SaveSlotStatus.Valid, document.DisplayName, document.UtcSavedAt));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { slots.Add(new(slot, SaveSlotStatus.Invalid, "无法读取", Error: error.Message)); }
        }
        return slots.AsReadOnly();
    }

    internal static void ValidateDocument(SaveGameDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion != 1 || document.Snapshot is null || document.Snapshot.Version != SessionSnapshot.CurrentVersion ||
            !SaveGameIdentity.IsHash(document.LibraryFingerprint) || !SaveGameIdentity.IsHash(document.StoryFingerprint) ||
            string.IsNullOrWhiteSpace(document.DisplayName) || document.DisplayName.Length > 80 || document.DisplayName.Any(char.IsControl) ||
            document.UtcSavedAt == default || document.UtcSavedAt.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Invalid save document metadata or version.");
        if (document.Overlay is { } overlay && (overlay.FormatVersion != 1 || overlay.Party is not null || overlay.World is not null))
            throw new InvalidDataException("Save story overlays must reference shared library data, not embed party/world resources.");
        if (!document.StoryFingerprint.Equals(SaveGameIdentity.ForStory(document.Overlay), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save story identity does not match its embedded overlay.");
    }

    private void ValidateDirectory()
    {
        foreach (var protectedPath in _protectedDirectories)
            if (Within(_directory, protectedPath)) throw new InvalidOperationException("Save directory must be outside protected resource directories.");
        RejectResourceAncestors(_directory);
        // Automatic protection also covers callers that do not know the original
        // installation path. Walk existing parents without creating anything.
        for (DirectoryInfo? parent = new(_directory); parent is not null; parent = parent.Parent)
        {
            if (parent.Exists && parent.LinkTarget is not null)
            {
                var resolved = parent.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                if (resolved is not null)
                {
                    var actual = Path.GetFullPath(Path.Combine(resolved, Path.GetRelativePath(parent.FullName, _directory)));
                    RejectResourceAncestors(actual);
                    foreach (var protectedPath in _protectedDirectories)
                        if (Within(actual, protectedPath)) throw new InvalidOperationException("Save directory resolves into protected resources.");
                }
            }
        }
    }
    private static void RejectResourceAncestors(string directory)
    {
        for (DirectoryInfo? parent = new(directory); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, "RANGER.IDX")) && File.Exists(Path.Combine(parent.FullName, "RANGER.GRP")) ||
                File.Exists(Path.Combine(parent.FullName, "manifest.json")) && System.IO.Directory.Exists(Path.Combine(parent.FullName, "data")) &&
                System.IO.Directory.Exists(Path.Combine(parent.FullName, "assets")))
                throw new InvalidOperationException("Save directory cannot be inside original resources or a game library.");
    }
    private static bool Within(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return path.Equals(fullRoot, comparison) || path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }
    private static void RequireFields(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object || fields.Any(field => !element.TryGetProperty(field, out _)))
            throw new InvalidDataException("Save is missing required schema fields.");
    }
    private static void RequireRecordFields<T>(JsonElement element) => RequireFields(element, typeof(T).GetProperties()
        .Where(property => property.GetMethod is { IsStatic: false })
        .Select(property => JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)).ToArray());
    private static void RequireSnapshotSchema(JsonElement snapshot)
    {
        // Version 1 writes every field, including explicit nulls. A correct
        // checksum must not allow omitted values to silently acquire defaults.
        RequireRecordFields<SessionSnapshot>(snapshot);
        RequireRecordFields<GridPosition>(snapshot.GetProperty("position"));
        var travel = snapshot.GetProperty("travel");
        if (travel.ValueKind != JsonValueKind.Null)
        {
            RequireRecordFields<WorldTravelSnapshot>(travel);
            foreach (var field in new[] { "position", "ship_position", "ship_bow" })
                RequireRecordFields<GridPosition>(travel.GetProperty(field));
        }
        var party = snapshot.GetProperty("party");
        if (party.ValueKind != JsonValueKind.Null) RequireRecordFields<PartySnapshot>(party);
        var events = snapshot.GetProperty("events");
        if (events.ValueKind != JsonValueKind.Null) RequireRecordFields<EventRunnerIdleSnapshot>(events);
        var camera = snapshot.GetProperty("camera");
        if (camera.ValueKind != JsonValueKind.Null) RequireFields(camera, "x", "y");
        var scenes = snapshot.GetProperty("scenes");
        if (scenes.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Saved scenes must be an array.");
        foreach (var scene in scenes.EnumerateArray()) RequireRecordFields<SceneStateSnapshot>(scene);
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Save contains duplicate JSON properties.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
