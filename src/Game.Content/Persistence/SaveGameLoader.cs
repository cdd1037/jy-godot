using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Session;

namespace JinYong.Content.Persistence;

public sealed record RestoredGame(ScenePackage View, GameSession Session);

/// <summary>Prepares an isolated candidate. The host validates/presents it before replacing the active game.</summary>
public static class SaveGameLoader
{
    public static RestoredGame Restore(GameLibrary library, SaveGameDocument document)
    {
        SaveGameStore.ValidateDocument(document);
        if (!document.LibraryFingerprint.Equals(SaveGameIdentity.ForLibrary(library), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The save belongs to a different resource library.");
        var snapshot = document.Snapshot;
        // Overlay programs belong to their original entry scene, which can differ
        // from the currently saved scene after travel. Do not replay the opening.
        var current = library.OpenScene(snapshot.SceneId, snapshot.Position, validateSpawn: false);
        var story = document.Overlay is { } overlay
            ? library.OpenScene(overlay.SceneId, overlay: overlay, validateSpawn: false).Story
            : current.Story;
        var candidate = GameSession.Restore(current.Scene, story, snapshot, library.MainMap, library.Battles);
        var view = candidate.IsWorld ? library.OpenWorldView(candidate) : current with { Story = candidate.Story };
        return new(view, candidate);
    }
}
