using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Content.Persistence;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Independently authored tiny central library; no DOS assets or save data.
[TestClass]
public sealed class SaveGameLoaderTests
{
    private const string Atlas = "assets/sprites/atlas.png";
    private static StoryDocument Overlay() => new()
    {
        SceneId = 0, InitialProgramId = 100, VariableNames = new() { [0] = "save-test:opening/runs" },
        Programs = [new(100, [new(0, EventOperation.AddVariable, [0, 1]), new(1, EventOperation.End, [])])],
        Dialogues = [new(100, ["嵌入存档的独立剧情"])]
    };

    [TestMethod]
    public void RestoreUsesOverlayEntrySceneAndSavedCurrentSceneWithoutReplayingOpening()
    {
        var library = GameLibrary.Load(Fixture()); var overlay = Overlay();
        var source = Session(library, overlay);
        Assert.IsTrue(source.Events!.TryStart(100, 0, source.Player.Position));
        source.Advance(TimeSpan.Zero);
        ((IEventWorld)source).SetVariable(0, 9);
        source.World.SetTile(0, MapLayer.Ground, new(3, 4), 4);
        source.World.SetEventField(1, 2, 2, 100);
        var snapshot = source.CaptureSnapshot();
        var save = SaveGameDocument.Create(snapshot, library, overlay, "异图存档");
        var restored = SaveGameLoader.Restore(library, save);
        Assert.AreEqual(0, restored.Session.Story!.SceneId);
        Assert.AreEqual(1, restored.Session.World.CurrentSceneId);
        Assert.AreEqual(1, restored.View.Document.LegacySceneId);
        Assert.AreSame(library, restored.View.Library);
        Assert.AreSame(restored.Session.Story, restored.View.Story);
        Assert.AreNotSame(source, restored.Session);
        Assert.AreSame(restored.Session.Scene, restored.Session.World.Scene(1));
        Assert.AreEqual(9, ((IEventWorld)restored.Session).GetVariable(0));
        Assert.AreEqual(4, restored.Session.World.Tile(0, MapLayer.Ground, new(3, 4)));
        Assert.AreEqual(100, restored.Session.World.EventField(1, 2, 2));
        Assert.AreEqual(4, restored.Session.Scene.At(MapLayer.Building, snapshot.Position));
        Assert.AreEqual(snapshot.Events!.RandomState, restored.Session.CaptureSnapshot().Events!.RandomState);
        CollectionAssert.AreEqual(snapshot.Events.CompletedPrograms.ToArray(), restored.Session.CaptureSnapshot().Events!.CompletedPrograms.ToArray());
        restored.Session.World.SetTile(0, MapLayer.Ground, new(3, 4), 0);
        Assert.AreEqual(4, source.World.Tile(0, MapLayer.Ground, new(3, 4)));
    }

    [TestMethod]
    public void ActualStoredOverlaySurvivesRestartAndLibraryRelocation()
    {
        var path = Fixture(); var library = GameLibrary.Load(path); var overlay = Overlay();
        var save = SaveGameDocument.Create(Session(library, overlay).CaptureSnapshot(), library, overlay, "可迁移");
        var store = new SaveGameStore(Path.Combine(Scratch(), "saves"), path); store.Save(2, save);
        var relocated = Path.Combine(Scratch(), "relocated");
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(relocated, Path.GetRelativePath(path, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        var movedLibrary = GameLibrary.Load(relocated);
        var fromDisk = new SaveGameStore(store.Directory).Read(2);
        var restored = SaveGameLoader.Restore(movedLibrary, fromDisk);
        Assert.AreEqual(SaveGameIdentity.ForLibrary(library), SaveGameIdentity.ForLibrary(movedLibrary));
        Assert.AreSame(movedLibrary, restored.View.Library);
        Assert.AreEqual("嵌入存档的独立剧情", restored.Session.Story!.Dialogues[100].Pages[0]);
        Assert.IsFalse(File.ReadAllText(store.GetSlotPath(2)).Contains(path, StringComparison.Ordinal));
        Assert.IsFalse(Directory.Exists(Path.Combine(relocated, "scenes")));
    }

    [TestMethod]
    public void InvalidSnapshotStoryOrChangedLibraryCannotMutateTheActiveSession()
    {
        var path = Fixture(); var library = GameLibrary.Load(path); var overlay = Overlay();
        var source = Session(library, overlay); var before = JsonSerializer.Serialize(source.CaptureSnapshot(), ContentJson.Options);
        var save = SaveGameDocument.Create(source.CaptureSnapshot(), library, overlay, "原档");
        Assert.ThrowsExactly<InvalidDataException>(() => SaveGameLoader.Restore(library, save with { Overlay = overlay with { Dialogues = [] } }));
        Assert.ThrowsExactly<ArgumentException>(() => SaveGameLoader.Restore(library, save with { Snapshot = save.Snapshot with { Position = new(-1, 0) } }));
        var manifest = library.Manifest with { Version = "changed" };
        ContentJson.Write(Path.Combine(path, "manifest.json"), manifest);
        Assert.ThrowsExactly<InvalidDataException>(() => SaveGameLoader.Restore(GameLibrary.Load(path), save));
        Assert.AreEqual(before, JsonSerializer.Serialize(source.CaptureSnapshot(), ContentJson.Options));
        var blob = Path.Combine(path, "data/world.layers.bin"); var bytes = File.ReadAllBytes(blob); bytes[0] ^= 2; File.WriteAllBytes(blob, bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => SaveGameLoader.Restore(library, save)); // loaded manifest cannot conceal changed disk bytes
    }

    [TestMethod]
    public void WorldCheckpointPreparesWorldBankWhileKeepingLastLocalSceneState()
    {
        var library = GameLibrary.Load(Fixture(world: true)); var overlay = Overlay(); var source = Session(library, overlay);
        var travel = library.GameData.TravelStart! with { Position = new(200, 201), Facing = Direction.Left };
        var snapshot = source.CaptureSnapshot() with { IsWorld = true, Travel = travel };
        var restored = SaveGameLoader.Restore(library, SaveGameDocument.Create(snapshot, library, overlay, "江湖"));
        Assert.IsTrue(restored.Session.IsWorld);
        Assert.AreEqual("base:world", restored.View.Document.Id);
        Assert.AreSame(library.MainMap, restored.Session.MainMap);
        Assert.AreEqual(new GridPosition(200, 201), restored.Session.Player.Position);
        Assert.AreEqual(1, restored.Session.World.CurrentSceneId);
        Assert.AreEqual(snapshot.Position, restored.Session.CaptureSnapshot().Position);
        Assert.AreSame(restored.Session.Story, restored.View.Story);
    }

    private static GameSession Session(GameLibrary library, StoryDocument overlay)
    {
        var story = library.OpenScene(0, overlay: overlay).Story;
        // A script may leave the actor on a blocking tile. Neither opening replay
        // nor normal spawn validation is appropriate for checkpoint restoration.
        var current = library.OpenScene(1, new(10, 11), validateSpawn: false);
        return new(current.Scene, story, worldMap: library.MainMap, travelStart: library.GameData.TravelStart,
            startInitialProgram: false, sceneIdOverride: 1, validateSpawn: false);
    }

    private static string Fixture(bool world = false)
    {
        var path = Path.Combine(Scratch(), "library"); Directory.CreateDirectory(Path.Combine(path, "data"));
        Directory.CreateDirectory(Path.Combine(path, "assets/sprites"));
        var scenes = new LibraryScene[2]; var layers = new List<short>();
        for (var id = 0; id < 2; id++)
        {
            var metadata = new short[26]; metadata[0] = (short)id; metadata[14] = 19; metadata[15] = 20;
            scenes[id] = new() { SceneId = id, Name = $"Scene {id}", HasLegacyMetadata = true, Metadata = metadata,
                SpawnX = 19, SpawnY = 20, SpriteBank = $"scene/{id:D3}" };
            var map = MovementTests.EmptyLayers();
            if (id == 1) map[4096 + 11 * 64 + 10] = 4;
            layers.AddRange(map);
        }
        File.WriteAllBytes(Path.Combine(path, "data/world.layers.bin"), Words(layers));
        File.WriteAllBytes(Path.Combine(path, "data/world.events.bin"), new byte[8800]);
        File.WriteAllBytes(Path.Combine(path, "assets/palette.rgb6"), new byte[768]);
        File.WriteAllBytes(Path.Combine(path, Atlas), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII="));
        const string key = "synthetic-shared-pixel";
        Dictionary<int, string> Bank()
        {
            var bank = new Dictionary<int, string> { [0] = key, [4] = key };
            foreach (var first in new[] { 5002, 5016, 5030, 5044, 7430, 7438, 7446, 7454 })
                for (var offset = 0; offset <= (first < 7430 ? 12 : 6); offset += 2) bank[first + offset] = key;
            return bank;
        }
        var banks = new Dictionary<string, Dictionary<int, string>> { ["scene/000"] = Bank(), ["scene/001"] = Bank(), ["portrait"] = new() { [0] = key } };
        if (world) banks["world"] = Bank();
        ContentJson.Write(Path.Combine(path, "data/scene-index.json"), scenes);
        ContentJson.Write(Path.Combine(path, "data/gameplay.json"), new GameDataDocument
        {
            Programs = [new(1, [new(0, EventOperation.End, [])])],
            TravelStart = world ? new(new(20, 21), Direction.Down, false, new(100, 100), new(101, 100), Direction.Right) : null
        });
        ContentJson.Write(Path.Combine(path, "data/sprites.json"), new SharedSpriteCatalog
        { Assets = new() { [key] = new SpriteAsset { Atlas = Atlas, Width = 1, Height = 1 } }, Banks = banks });
        ContentJson.Write(Path.Combine(path, "data/audio.json"), new LibraryAudio());
        ContentJson.Write(Path.Combine(path, "data/source-index.json"), new Dictionary<string, SourceResource>());
        if (world) File.WriteAllBytes(Path.Combine(path, "data/main-map.bin"), new byte[WorldMapDefinition.CellCount * WorldMapDefinition.LayerCount * 2]);
        var hashes = Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(path, file).Replace('\\', '/'), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        ContentJson.Write(Path.Combine(path, "manifest.json"), new GameLibraryManifest { DefaultScene = 1, MainMap = world ? "data/main-map.bin" : null, FileHashes = hashes });
        return path;
    }
    private static byte[] Words(IEnumerable<short> source)
    {
        var words = source.ToArray(); var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), words[index]);
        return bytes;
    }
    private static string Scratch() => Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"));
}
