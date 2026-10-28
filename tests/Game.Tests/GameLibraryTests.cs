using System.Buffers.Binary;
using System.Security.Cryptography;
using JinYong.Content.Legacy;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Small independently authored central libraries; never copied original DOS resources.
[TestClass]
public sealed class GameLibraryTests
{
    private const string Atlas = "assets/sprites/atlas-000.png";

    [TestMethod]
    public void SceneViewsReuseTheSameImmutableProgramDefinitions()
    {
        var path = Fixture();
        WriteTracked(path, "data/gameplay.json", new GameDataDocument
        {
            Programs = [new(1, [new(0, EventOperation.End, [])])],
            Dialogues = [new(1, ["共享对白"])]
        });
        var library = GameLibrary.Load(path);
        var first = library.OpenScene(0);
        var second = library.OpenScene(1);
        Assert.AreSame(first.Story!.Programs[1], second.Story!.Programs[1]);
        Assert.AreSame(first.Story.Dialogues[1], second.Story.Dialogues[1]);
        Assert.AreSame(first.Story.World, second.Story.World);
    }

    [TestMethod]
    public void SceneViewsShareLibraryDefinitionsAndAtlasesButSessionsOwnMutableState()
    {
        var path = Fixture();
        var before = Snapshot(path);
        var library = GameLibrary.Load(path);
        var first = library.OpenScene(0, overlay: Overlay(0));
        var second = library.OpenScene(1, overlay: Overlay(1));
        Assert.AreSame(library, first.Library);
        Assert.AreSame(library, second.Library);
        Assert.AreSame(library.World, first.Story!.World);
        Assert.AreSame(first.Story.World, second.Story!.World);
        Assert.AreEqual(0, first.Document.LegacySceneId);
        Assert.AreEqual(1, second.Document.LegacySceneId);
        var firstAsset = first.Document.Sprites.Single(sprite => sprite.LegacyId == 0);
        var secondAsset = second.Document.Sprites.Single(sprite => sprite.LegacyId == 2);
        Assert.AreEqual(firstAsset.Atlas, secondAsset.Atlas);
        Assert.AreEqual(firstAsset.X, secondAsset.X);
        Assert.AreEqual(firstAsset.Y, secondAsset.Y);
        Assert.AreEqual(first.Resolve(firstAsset.Atlas), second.Resolve(secondAsset.Atlas));

        var firstSession = new GameSession(first.Scene, first.Story);
        var secondSession = new GameSession(second.Scene, second.Story);
        Assert.AreSame(firstSession.Scene, firstSession.World.Scene(0));
        Assert.AreSame(secondSession.Scene, secondSession.World.Scene(1));
        firstSession.World.SetTile(1, MapLayer.Ground, new(19, 20), 8);
        firstSession.World.SetEventField(1, 2, 2, 123);
        Assert.AreEqual(8, firstSession.World.Tile(1, MapLayer.Ground, new(19, 20)));
        Assert.AreEqual(2, secondSession.World.Tile(1, MapLayer.Ground, new(19, 20)));
        Assert.AreEqual(0, secondSession.World.EventField(1, 2, 2));
        Assert.AreEqual(2, library.World.Scene(1).ToSceneDefinition().At(MapLayer.Ground, new(19, 20)));
        CollectionAssert.AreEqual(before, Snapshot(path));
        Assert.IsFalse(Directory.Exists(Path.Combine(path, "scenes")));
        Assert.HasCount(1, Directory.GetFiles(path, "*.png", SearchOption.AllDirectories));
    }

    [TestMethod]
    public void EmptyGameplayOpensDefaultSceneAndSpawnOverrideOnlyChangesTheView()
    {
        var path = Fixture();
        var library = GameLibrary.Load(path);
        var defaultView = library.OpenScene();
        var overrideView = library.OpenScene(0, new(10, 11));
        Assert.AreEqual(1, defaultView.Document.LegacySceneId);
        Assert.AreEqual(new GridPosition(19, 20), defaultView.Scene.Spawn);
        Assert.AreEqual(new GridPosition(10, 11), overrideView.Scene.Spawn);
        Assert.AreEqual(new GridPosition(19, 20), library.World.Scene(0).ToSceneDefinition().Spawn);
        Assert.IsTrue(defaultView.Story is null || defaultView.Story.InitialProgramId is null);
        Assert.AreSame(library, defaultView.Library);
        Assert.ThrowsExactly<ArgumentException>(() => library.OpenScene(99));
    }

    [TestMethod]
    public void CorruptedWorldBlobIsRejectedByItsRecordedHash()
    {
        var path = Fixture();
        var blob = Path.Combine(path, "data/world.layers.bin");
        var bytes = File.ReadAllBytes(blob);
        bytes[0] ^= 2;
        File.WriteAllBytes(blob, bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    [DataRow("data/world.layers.bin")]
    [DataRow("data/world.events.bin")]
    public void CorrectHashDoesNotPermitTruncatedWorldRecords(string relative)
    {
        var path = Fixture();
        var target = Path.Combine(path, relative);
        File.WriteAllBytes(target, File.ReadAllBytes(target)[..^2]);
        RefreshHash(path, relative);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void ReferencedAtlasMustBeTrackedEvenWhenTheFileExists()
    {
        var path = Fixture();
        var manifest = Manifest(path);
        manifest.FileHashes.Remove(Atlas);
        ContentJson.Write(Path.Combine(path, "manifest.json"), manifest);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void UnknownSharedAssetReferenceInBankIsRejected()
    {
        var path = Fixture();
        var sprites = ContentJson.Read<SharedSpriteCatalog>(Path.Combine(path, "data/sprites.json"));
        sprites.Banks["scene/001"][2] = new string('f', 64);
        WriteTracked(path, "data/sprites.json", sprites);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void SceneIndexMustReferenceAnExistingBank()
    {
        var path = Fixture();
        var scenes = ContentJson.Read<LibraryScene[]>(Path.Combine(path, "data/scene-index.json"));
        scenes[1] = scenes[1] with { SpriteBank = "scene/missing" };
        WriteTracked(path, "data/scene-index.json", scenes);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    [DataRow("../outside.bin")]
    [DataRow("C:/outside.bin")]
    public void ManifestPathsCannotEscapeTheLibrary(string invalid)
    {
        var path = Fixture();
        ContentJson.Write(Path.Combine(path, "manifest.json"), Manifest(path) with { WorldLayers = invalid });
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void TrackedPathsCannotEscapeTheLibraryEvenWithADeclaredHash()
    {
        var path = Fixture();
        var manifest = Manifest(path);
        manifest.FileHashes["../outside.bin"] = new string('0', 64);
        ContentJson.Write(Path.Combine(path, "manifest.json"), manifest);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void SharedSpriteCannotExtendBeyondTheRealAtlasDimensions()
    {
        var path = Fixture();
        var sprites = ContentJson.Read<SharedSpriteCatalog>(Path.Combine(path, "data/sprites.json"));
        var key = sprites.Assets.Keys.Single();
        sprites.Assets[key] = sprites.Assets[key] with { Width = 2 };
        WriteTracked(path, "data/sprites.json", sprites);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void DuplicateSceneIdsAreRejectedBeforeConstructingViews()
    {
        var path = Fixture();
        var scenes = ContentJson.Read<LibraryScene[]>(Path.Combine(path, "data/scene-index.json"));
        scenes[1] = scenes[1] with { SceneId = scenes[0].SceneId };
        WriteTracked(path, "data/scene-index.json", scenes);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void MetadataMustContainExactlyTwentySixWords()
    {
        var path = Fixture();
        var scenes = ContentJson.Read<LibraryScene[]>(Path.Combine(path, "data/scene-index.json"));
        scenes[0] = scenes[0] with { Metadata = new short[25] };
        WriteTracked(path, "data/scene-index.json", scenes);
        Assert.ThrowsExactly<InvalidDataException>(() => GameLibrary.Load(path));
    }

    [TestMethod]
    public void SourceVaultPreservesGameResourcesAndExcludesProgramsDriversAndSaves()
    {
        var root = Scratch();
        var source = Path.Combine(root, "source");
        var output = Path.Combine(root, "library");
        Directory.CreateDirectory(source);
        MinimalOriginal(source);
        var included = new Dictionary<string, byte[]>
        {
            ["GAME01.XMI"] = [1, 2, 3], ["E01.WAV"] = [4, 5, 6], ["HERO.STA"] = [7, 8],
            ["FONT.C16"] = [9, 10], ["TITLE.BIG"] = [11, 12], ["ENDCOL.COL"] = [13, 14]
        };
        var excluded = new[] { "Z.COM", "Z.DAT", "DOS4GW.EXE", "RANGER1.GRP", "RANGER1.IDX", "S1", "D1", "CT-VOICE.DRV", "JY.INI", "README.TXT" };
        foreach (var pair in included) File.WriteAllBytes(Path.Combine(source, pair.Key), pair.Value);
        foreach (var name in excluded) File.WriteAllBytes(Path.Combine(source, name), [99, 88]);
        var before = Snapshot(source);
        var report = new LegacyGameImporter().Import(new(source, output, IncludeStory: false));
        var library = GameLibrary.Load(output);
        Assert.AreEqual(1, report.Scenes);
        foreach (var (name, bytes) in included)
        {
            Assert.IsTrue(library.Sources.ContainsKey(name), name);
            var resource = library.Sources[name];
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), resource.Sha256);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(ScenePackageReader.Resolve(output, resource.Storage)));
        }
        foreach (var name in excluded) Assert.IsFalse(library.Sources.ContainsKey(name), name);
        Assert.IsTrue(library.Sources.ContainsKey("SMP000"));
        Assert.IsTrue(library.Sources.ContainsKey("HDGRP.GRP"));
        Assert.AreEqual(library.Sources["E01.WAV"].Storage, library.Audio.Effects[1]);
        Assert.AreEqual(library.Sources["GAME01.XMI"].Storage, library.Audio.Music[1]);
        Assert.IsFalse(Directory.Exists(Path.Combine(output, "scenes")));
        Assert.IsFalse(Directory.GetFiles(output, "*", SearchOption.AllDirectories).Any(file => excluded.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)));
        CollectionAssert.AreEqual(before, Snapshot(source));
    }

    [TestMethod]
    public void ImportOutputCannotBeTheOriginalDirectoryOrItsDescendant()
    {
        var source = Path.Combine(Scratch(), "source");
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "marker"), [1]);
        var before = Snapshot(source);
        Assert.ThrowsExactly<InvalidOperationException>(() => new LegacyGameImporter().Import(new(source, source, IncludeStory: false)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new LegacyGameImporter().Import(new(source, Path.Combine(source, "output"), IncludeStory: false)));
        CollectionAssert.AreEqual(before, Snapshot(source));
    }

    private static StoryDocument Overlay(int id) => new()
    {
        SceneId = id, Programs = [new(1, [new(0, EventOperation.End, [])])]
    };

    private static string Fixture()
    {
        var path = Path.Combine(Scratch(), "library");
        Directory.CreateDirectory(Path.Combine(path, "data"));
        Directory.CreateDirectory(Path.Combine(path, "assets/sprites"));
        var scenes = new LibraryScene[2];
        var allLayers = new List<short>();
        for (var id = 0; id < 2; id++)
        {
            var metadata = new short[26]; metadata[0] = (short)id; metadata[14] = 19; metadata[15] = 20;
            scenes[id] = new() { SceneId = id, Name = $"Scene {id}", HasLegacyMetadata = true,
                Metadata = metadata, SpawnX = 19, SpawnY = 20, SpriteBank = $"scene/{id:D3}" };
            var layers = MovementTests.EmptyLayers();
            Array.Fill(layers, (short)(id * 2), 0, 4096);
            allLayers.AddRange(layers);
        }
        File.WriteAllBytes(Path.Combine(path, "data/world.layers.bin"), Words(allLayers));
        File.WriteAllBytes(Path.Combine(path, "data/world.events.bin"), new byte[2 * 4400]);
        File.WriteAllBytes(Path.Combine(path, "assets/palette.rgb6"), new byte[768]);
        File.WriteAllBytes(Path.Combine(path, Atlas), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII="));
        File.WriteAllBytes(Path.Combine(path, "assets/sprites/atlas-000.indices"), [1]);
        File.WriteAllBytes(Path.Combine(path, "assets/sprites/atlas-000.alpha"), [255]);
        var hashBytes = new byte[18];
        BinaryPrimitives.WriteInt32LittleEndian(hashBytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(hashBytes.AsSpan(4), 1);
        hashBytes[16] = 1; hashBytes[17] = 255;
        var key = Convert.ToHexStringLower(SHA256.HashData(hashBytes));
        var sprite = new SpriteAsset { LegacyId = 0, Atlas = Atlas, Width = 1, Height = 1 };
        Dictionary<int, string> Bank(int mapId)
        {
            var result = new Dictionary<int, string> { [mapId] = key };
            foreach (var first in new[] { 5002, 5016, 5030, 5044 })
                for (var frame = 0; frame <= 12; frame += 2) result[first + frame] = key;
            return result;
        }
        ContentJson.Write(Path.Combine(path, "data/scene-index.json"), scenes);
        ContentJson.Write(Path.Combine(path, "data/gameplay.json"), new GameDataDocument());
        ContentJson.Write(Path.Combine(path, "data/sprites.json"), new SharedSpriteCatalog
        {
            Assets = new() { [key] = sprite }, Banks = new() { ["scene/000"] = Bank(0), ["scene/001"] = Bank(2), ["portrait"] = new() { [0] = key } }
        });
        ContentJson.Write(Path.Combine(path, "data/audio.json"), new LibraryAudio());
        ContentJson.Write(Path.Combine(path, "data/source-index.json"), new Dictionary<string, SourceResource>());
        ContentJson.Write(Path.Combine(path, "manifest.json"), new GameLibraryManifest { DefaultScene = 1, FileHashes = Hashes(path) });
        return path;
    }

    private static void MinimalOriginal(string source)
    {
        void Pair(string prefix, params byte[][] entries)
        {
            uint end = 0;
            File.WriteAllBytes(Path.Combine(source, prefix + ".IDX"), LegacyFormatTests.Index(entries.Select(bytes => { end += (uint)bytes.Length; return end; }).ToArray()));
            File.WriteAllBytes(Path.Combine(source, prefix + ".GRP"), entries.SelectMany(bytes => bytes).ToArray());
        }
        var metadata = new byte[52]; metadata[2] = (byte)'A';
        BinaryPrimitives.WriteInt16LittleEndian(metadata.AsSpan(28), 19);
        BinaryPrimitives.WriteInt16LittleEndian(metadata.AsSpan(30), 20);
        Pair("RANGER", new byte[836], new byte[182], new byte[190], metadata, new byte[136], new byte[30]);
        Pair("ALLSIN", Words(MovementTests.EmptyLayers()));
        Pair("ALLDEF", new byte[4400]);
        File.WriteAllBytes(Path.Combine(source, "MMAP.COL"), new byte[768]);
        byte[] sprite = [1, 0, 1, 0, 0, 0, 0, 0, 3, 0, 1, 1];
        var spriteEnds = Enumerable.Repeat((uint)sprite.Length, 2529).ToArray();
        spriteEnds[^1] = 0;
        File.WriteAllBytes(Path.Combine(source, "SDX000"), LegacyFormatTests.Index(spriteEnds));
        File.WriteAllBytes(Path.Combine(source, "SMP000"), [.. sprite, .. sprite]);
        Pair("HDGRP", sprite);
    }

    private static string Scratch() => Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"));
    private static byte[] Words(IEnumerable<short> words)
    {
        var source = words.ToArray(); var result = new byte[source.Length * 2];
        for (var index = 0; index < source.Length; index++) BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(index * 2), source[index]);
        return result;
    }
    private static Dictionary<string, string> Hashes(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .Where(file => Path.GetFileName(file) != "manifest.json").ToDictionary(file => Path.GetRelativePath(path, file).Replace('\\', '/'),
            file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
    private static string[] Snapshot(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(path, file).Replace('\\', '/') + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))
        .Order(StringComparer.Ordinal).ToArray();
    private static GameLibraryManifest Manifest(string path) => ContentJson.Read<GameLibraryManifest>(Path.Combine(path, "manifest.json"));
    private static void RefreshHash(string path, string relative)
    {
        var manifest = Manifest(path);
        manifest.FileHashes[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(path, relative))));
        ContentJson.Write(Path.Combine(path, "manifest.json"), manifest);
    }
    private static void WriteTracked<T>(string path, string relative, T value)
    {
        ContentJson.Write(Path.Combine(path, relative), value);
        RefreshHash(path, relative);
    }
}
