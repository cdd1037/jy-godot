using JinYong.Content.Legacy;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

/// <summary>One original-resource library import shared by all scene-story tests.</summary>
internal static class OriginalLibraryFixture
{
    private static readonly Lazy<GameLibrary> Imported = new(Import);
    public static GameLibrary Library => Imported.Value;

    private static GameLibrary Import()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to validate original assets.");
        var output = Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"), "library");
        new LegacyGameImporter().Import(new LibraryImportOptions(source!, output));
        var library = GameLibrary.Load(output);
        var verification = Path.Combine(LegacyFixture.Root, ".local", "verification");
        Directory.CreateDirectory(verification);
        File.WriteAllText(Path.Combine(verification, "latest-original-library.txt"), output);
        return library;
    }

    public static ScenePackage OpenFreeScene(int sceneId)
    {
        var library = Library;
        var map = library.World.Scene(sceneId).ToSceneDefinition();
        var spawn = Enumerable.Range(0, SceneDefinition.TileCount).Select(index => new GridPosition(index % 64, index / 64))
            .First(position => map.At(MapLayer.Event, position) == -1 &&
                SceneMovementRules.Check(map, position, position) == MovementBlock.None);
        return library.OpenScene(sceneId, spawn);
    }
}
