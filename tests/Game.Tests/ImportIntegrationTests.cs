using System.Security.Cryptography;
using JinYong.Content.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

/// <summary>
/// Regression coverage for reading old format-1/2 scene packages. All inputs are
/// independently authored format fixtures; this is not the production import path.
/// New game data is imported through LegacyGameImporter into one shared library.
/// </summary>
[TestClass]
public sealed class ImportIntegrationTests
{
    // Generate a historical map-only package to exercise the compatibility reader.
    [TestMethod]
    public void ImportRoundTripPreservesLayersEventsSpritesAndOriginalFiles()
    {
        var (source, output) = LegacyFixture.Create();
        var before = Fingerprints(source);
        var report = new LegacySceneImporter().Import(new(source, output, IncludeStory: false));
        var package = ScenePackageReader.Load(output);
        Assert.AreEqual("格式測試場", report.SceneName);
        Assert.AreEqual("base:scene/070", package.Scene.Id);
        Assert.AreEqual(9, package.Manifest.SourceHashes.Count);
        Assert.AreEqual(new GridPosition(19, 20), package.Scene.Spawn);
        Assert.AreEqual(MovementBlock.Event, new GameSession(package.Scene).TryMove(Direction.Right).Block);
        Assert.AreEqual((short)358, package.Scene.At(MapLayer.Ground, new(24, 20)));
        Assert.AreEqual(200, package.Scene.Events.Count);
        Assert.AreEqual(6, package.Scene.Events[0].Picture);
        Assert.AreEqual(34, package.Document.Sprites.Length);
        foreach (var pair in before) Assert.AreEqual(pair.Value, Fingerprints(source)[pair.Key]);
        foreach (var sprite in package.Document.Sprites.Where(sprite => sprite.Width > 0))
            Assert.IsTrue(File.Exists(package.Resolve(sprite.Atlas)));
        var verification = Path.Combine(LegacyFixture.Root, ".local", "verification");
        Directory.CreateDirectory(verification);
        File.WriteAllText(Path.Combine(verification, "latest-fixture.txt"), output);
    }

    // The test-only writer retains the old transactional output contract.
    [TestMethod]
    public void ImportRejectsBlockedSpawnWithoutPublishingPartialPackage()
    {
        var (source, output) = LegacyFixture.Create();
        Assert.ThrowsExactly<ArgumentException>(() => new LegacySceneImporter().Import(new(source, output, Spawn: new(20, 20), IncludeStory: false)));
        Assert.IsFalse(Directory.Exists(output));
        Assert.AreEqual(0, Directory.GetDirectories(Path.GetDirectoryName(output)!, ".import-*").Length);
    }

    // Fixture generation must not overwrite its input or an existing old package.
    [TestMethod]
    public void ImportCannotWriteIntoOriginalDirectoryOrOverwriteAnExistingPackage()
    {
        var (source, output) = LegacyFixture.Create();
        Assert.ThrowsExactly<InvalidOperationException>(() => new LegacySceneImporter().Import(new(source, Path.Combine(source, "output"))));
        Directory.CreateDirectory(output);
        Assert.ThrowsExactly<IOException>(() => new LegacySceneImporter().Import(new(source, output)));
    }

    // Missing fixture resources keep the historical filename diagnostic.
    [TestMethod]
    public void MissingOriginalResourceReportsItsFilename()
    {
        var (source, output) = LegacyFixture.Create();
        File.Move(Path.Combine(source, "MMAP.COL"), Path.Combine(source, "MMAP.backup"));
        var error = Assert.ThrowsExactly<FileNotFoundException>(() => new LegacySceneImporter().Import(new(source, output, IncludeStory: false)));
        StringAssert.Contains(error.Message, "MMAP.COL");
        Assert.IsFalse(Directory.Exists(output));
    }

    // An old package still receives integrity checks when loaded for compatibility.
    [TestMethod]
    public void PackageChecksumDetectsDamagedLayers()
    {
        var (source, output) = LegacyFixture.Create();
        new LegacySceneImporter().Import(new(source, output, IncludeStory: false));
        File.WriteAllBytes(Path.Combine(output, "layers.bin"), [0]);
        var error = Assert.ThrowsExactly<InvalidDataException>(() => ScenePackageReader.Load(output));
        StringAssert.Contains(error.Message, "layers.bin");
    }

    [TestMethod]
    public void ContentPathsCannotEscapeTheirPackage()
    {
        var root = Path.Combine(LegacyFixture.Root, ".local", "example");
        Assert.ThrowsExactly<InvalidDataException>(() => ScenePackageReader.Resolve(root, "../outside.bin"));
        Assert.AreEqual(Path.Combine(root, "textures", "atlas.png"), ScenePackageReader.Resolve(root, "textures/atlas.png"));
    }

    private static Dictionary<string, string> Fingerprints(string directory) => Directory.GetFiles(directory).ToDictionary(
        path => Path.GetFileName(path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
}
