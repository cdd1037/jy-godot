using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Persistence;

namespace JinYong.Content.Persistence;

/// <summary>Portable remake save. No original install paths or resource copies are embedded.</summary>
public sealed record SaveGameDocument
{
    public int FormatVersion { get; init; } = 1;
    public required SessionSnapshot Snapshot { get; init; }
    public required string LibraryFingerprint { get; init; }
    public required string StoryFingerprint { get; init; }
    public StoryDocument? Overlay { get; init; }
    public required DateTimeOffset UtcSavedAt { get; init; }
    public required string DisplayName { get; init; }

    public static SaveGameDocument Create(SessionSnapshot snapshot, GameLibrary library, StoryDocument? overlay,
        string displayName, DateTimeOffset? utcSavedAt = null) => new()
    {
        Snapshot = snapshot, LibraryFingerprint = SaveGameIdentity.ForLibrary(library),
        StoryFingerprint = SaveGameIdentity.ForStory(overlay), Overlay = overlay,
        DisplayName = displayName.Trim(), UtcSavedAt = (utcSavedAt ?? DateTimeOffset.UtcNow).ToUniversalTime()
    };
}

public enum SaveSlotStatus { Empty, Valid, Invalid }
public sealed record SaveSlotInfo(int Slot, SaveSlotStatus Status, string DisplayName, DateTimeOffset? UtcSavedAt = null, string? Error = null);
