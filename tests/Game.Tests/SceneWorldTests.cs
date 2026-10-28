using System.Buffers.Binary;
using System.Text.Json;
using JinYong.Importing.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class SceneWorldTests
{
    private static WorldSceneDefinition Definition(int id, short condition = 1)
    {
        var metadata = new short[26];
        metadata[0] = (short)id;
        metadata[9] = condition;
        metadata[14] = 19;
        metadata[15] = 20;
        return new(id, $"Scene {id}", metadata, MovementTests.EmptyLayers(),
            Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray());
    }

    private static SceneWorldState State(int current, params int[] ids)
    {
        var definition = new WorldDefinition(ids.Select(id => Definition(id)).ToArray());
        return new(current, new SceneRuntimeState(definition.Scene(current).ToSceneDefinition()), definition);
    }

    [TestMethod]
    public void ActiveSceneSharesIdentityAndRemoteChangesAreSessionLocal()
    {
        var definition = new WorldDefinition([Definition(1), Definition(3)]);
        var active = new SceneRuntimeState(definition.Scene(1).ToSceneDefinition());
        var first = new SceneWorldState(1, active, definition);
        var second = new SceneWorldState(1, new SceneRuntimeState(definition.Scene(1).ToSceneDefinition()), definition);
        Assert.AreSame(active, first.Scene(-2));
        first.SetTile(1, MapLayer.Building, new(19, 20), 42);
        Assert.AreEqual(42, active.At(MapLayer.Building, new(19, 20)));
        first.SetTile(3, MapLayer.Building, new(19, 20), 10);
        first.SetEventField(3, 7, 2, 300);
        Assert.AreEqual(10, first.Tile(3, MapLayer.Building, new(19, 20)));
        Assert.AreEqual(300, first.EventField(3, 7, 2));
        Assert.AreEqual(0, second.Tile(3, MapLayer.Building, new(19, 20)));
        Assert.AreEqual(0, definition.Scene(3).ToSceneDefinition().Events[7][2]);
    }

    [TestMethod]
    public void ImmutableWorldRoundTripsJsonWithoutLeakingEventArrays()
    {
        var metadata = new short[26];
        var layers = MovementTests.EmptyLayers();
        var events = Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray();
        events[2][2] = 123;
        var scene = new WorldSceneDefinition(7, "家", metadata, layers, events);
        metadata[9] = 22;
        layers[0] = 44;
        events[2][2] = 999;
        scene.Events[2][2] = 888;
        Assert.AreEqual(0, scene.Metadata[9]);
        Assert.AreEqual(0, scene.Layers[0]);
        Assert.AreEqual(123, scene.Events[2][2]);
        var document = JsonSerializer.Serialize(new WorldDefinition([scene]), ContentJson.Options);
        var copy = JsonSerializer.Deserialize<WorldDefinition>(document, ContentJson.Options)!;
        Assert.AreEqual("家", copy.Scene(7).Name);
        Assert.AreEqual(123, copy.Scene(7).ToSceneDefinition().Events[2][2]);
        Assert.AreEqual(new GridPosition(0, 0), copy.Scene(7).ToSceneDefinition().Spawn);
    }

    [TestMethod]
    public void RemoteEventRelocationUpdatesRemoteMapAndMinusOneClearsInvokingTrigger()
    {
        var state = State(1, 1, 3);
        var trigger = new GridPosition(19, 20);
        state.SetTile(1, MapLayer.Event, trigger, 2);
        state.SetTile(3, MapLayer.Event, new(0, 0), 2);
        state.ChangeEvent(1, 2, trigger, [3, -1, 0, 0, 100, -2, -2, 20, 20, 20, 0, 10, 11]);
        Assert.AreEqual(-1, state.Tile(1, MapLayer.Event, trigger));
        Assert.AreEqual(-1, state.Tile(3, MapLayer.Event, new(0, 0)));
        Assert.AreEqual(2, state.Tile(3, MapLayer.Event, new(10, 11)));
        Assert.AreEqual(100, state.EventField(3, 2, 2));
        Assert.AreEqual(0, state.EventField(1, 2, 2));
    }

    [TestMethod]
    public void ScriptIncrementWrapsWordsAndDoesNotTreatMinusTwoAsKeep()
    {
        var state = State(1, 1, 3);
        state.SetEventField(3, 2, 2, short.MaxValue);
        state.SetEventField(3, 2, 3, -1);
        state.IncrementEventScripts(3, 2, -2, 1, -1, 9);
        Assert.AreEqual(short.MinValue, state.EventField(3, 2, 2));
        Assert.AreEqual(-2, state.EventField(3, 2, 3));
        Assert.AreEqual(9, state.EventField(3, 2, 4));
        Assert.AreEqual(0, state.EventField(1, 2, 4));
    }

    [TestMethod]
    public void LayerReplacementOnlyChangesMatchingCellsOnTargetLayerAndScene()
    {
        var state = State(1, 1, 3);
        state.SetTile(3, MapLayer.Decoration, new(4, 5), 6);
        state.SetTile(3, MapLayer.Ground, new(4, 5), 6);
        state.SetTile(3, MapLayer.Ground, new(4, 6), 6);
        Assert.AreEqual(2, state.ReplaceTiles(3, MapLayer.Ground, 6, 14));
        Assert.AreEqual(14, state.Tile(3, MapLayer.Ground, new(4, 5)));
        Assert.AreEqual(6, state.Tile(3, MapLayer.Decoration, new(4, 5)));
        Assert.AreEqual(0, state.Tile(1, MapLayer.Ground, new(4, 5)));
    }

    [TestMethod]
    public void OpenAllKeepsFourLegacyEntranceExceptionsAndOriginalDefinition()
    {
        var state = State(1, 1, 2, 38, 75, 80);
        state.OpenEntrance(2);
        Assert.AreEqual(0, state.MetadataWord(2, 9));
        state.OpenAllEntrances();
        Assert.AreEqual(0, state.MetadataWord(1, 9));
        Assert.AreEqual(2, state.MetadataWord(2, 9));
        Assert.AreEqual(2, state.MetadataWord(38, 9));
        Assert.AreEqual(1, state.MetadataWord(75, 9));
        Assert.AreEqual(1, state.MetadataWord(80, 9));
    }

    [TestMethod]
    public void FourteenBooksRequireCurrentPictureOfAllFourteenEvents()
    {
        var state = State(1, 1, 3);
        for (var index = 11; index < 25; index++) state.SetEventField(1, index, 7, 4664);
        Assert.IsTrue(state.HaveFourteenBooks());
        Assert.IsFalse(state.HaveFourteenBooks(3));
        state.SetEventField(1, 24, 7, 4662);
        Assert.IsFalse(state.HaveFourteenBooks());
    }

    [TestMethod]
    public void CompanionDepartureHidesKnownMapEventsWithoutRelocatingThem()
    {
        var state = State(1, 1, 44, 80);
        state.SetEventField(44, 1, 9, 17);
        state.SetEventField(44, 1, 10, 18);
        state.SetEventField(44, 1, 7, 8000);
        state.HideDepartedCompanions();
        Assert.AreEqual(-1, state.EventField(44, 1, 2));
        Assert.AreEqual(-1, state.EventField(44, 1, 7));
        Assert.AreEqual(0, state.EventField(44, 1, 8));
        Assert.AreEqual(17, state.EventField(44, 1, 9));
        Assert.AreEqual(18, state.EventField(44, 1, 10));
        Assert.AreEqual(-1, state.EventField(80, 1, 2));
    }

    [TestMethod]
    public void ShopRelocationUsesSuppliedRandomChoiceAndHidesCurrentShop()
    {
        var state = State(61, 1, 3, 40, 60, 61);
        state.SetEventField(61, 12, 7, 8256);
        state.SetEventField(3, 14, 9, 23);
        state.SetEventField(3, 14, 10, 24);
        state.MoveWeiXiaobaoShop(1);
        Assert.AreEqual(-1, state.EventField(61, 12, 7));
        Assert.AreEqual(938, state.EventField(3, 14, 2));
        Assert.AreEqual(8256, state.EventField(3, 14, 7));
        Assert.AreEqual(1, state.EventField(3, 14, 0));
        Assert.AreEqual(23, state.EventField(3, 14, 9));
        Assert.AreEqual(24, state.EventField(3, 14, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => state.MoveWeiXiaobaoShop(5));
    }

    [TestMethod]
    public void WorldImporterReadsEverySceneAndRejectsMismatchedArchives()
    {
        var scene = Definition(0);
        var metadata = new byte[52];
        metadata[2] = (byte)'H';
        BinaryPrimitives.WriteInt16LittleEndian(metadata.AsSpan(28), 19);
        BinaryPrimitives.WriteInt16LittleEndian(metadata.AsSpan(30), 20);
        var ranger = Archive([], [], [], metadata, [], []);
        var maps = Archive(Bytes(scene.Layers));
        var events = Archive(Bytes(scene.Events.SelectMany(words => words)));
        var world = LegacyWorldImporter.Read(ranger, maps, events);
        Assert.HasCount(1, world.Scenes);
        Assert.AreEqual("H", world.Scene(0).Name);
        Assert.AreEqual(new GridPosition(19, 20), world.Scene(0).ToSceneDefinition().Spawn);
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyWorldImporter.Read(ranger, Archive(new byte[10]), events));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyWorldImporter.Read(ranger, maps, Archive(new byte[4400], new byte[4400])));
    }

    [TestMethod]
    public void WorldImporterPreservesExtraMapSlotsWithoutInventingMetadata()
    {
        var scene = Definition(0);
        var metadata = new byte[52];
        var map = Bytes(scene.Layers);
        var events = Bytes(scene.Events.SelectMany(words => words));
        var world = LegacyWorldImporter.Read(Archive([], [], [], metadata, [], []), Archive(map, map), Archive(events, events));
        Assert.HasCount(2, world.Scenes);
        Assert.IsTrue(world.Scene(0).HasLegacyMetadata);
        Assert.IsFalse(world.Scene(1).HasLegacyMetadata);
        var runtime = new SceneWorldState(0, new SceneRuntimeState(world.Scene(0).ToSceneDefinition()), world);
        runtime.SetEventField(1, 2, 2, 99);
        Assert.AreEqual(99, runtime.EventField(1, 2, 2));
        runtime.OpenAllEntrances();
        Assert.AreEqual(0, runtime.MetadataWord(1, 9));
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualDosArchivesImportHundredMapsAndEightyFourMetadataRecords()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to validate original assets.");
        LegacyArchive Read(string prefix) => LegacyArchive.Parse(File.ReadAllBytes(Path.Combine(source!, prefix + ".IDX")),
            File.ReadAllBytes(Path.Combine(source!, prefix + ".GRP")));
        var world = LegacyWorldImporter.Read(Read("RANGER"), Read("ALLSIN"), Read("ALLDEF"));
        Assert.HasCount(100, world.Scenes);
        Assert.AreEqual(84, world.Scenes.Count(scene => scene.HasLegacyMetadata));
        Assert.AreEqual("主角的家", world.Scene(70).Name);
        Assert.AreEqual(691, world.Scene(70).ToSceneDefinition().Events[0][4]);
        Assert.IsFalse(world.Scene(99).HasLegacyMetadata);
        var runtime = new SceneWorldState(70, new SceneRuntimeState(world.Scene(70).ToSceneDefinition(new(19, 20))), world);
        runtime.HideDepartedCompanions();
        Assert.AreEqual(-1, runtime.EventField(80, 1, 2));
        runtime.OpenAllEntrances();
        Assert.AreEqual(0, runtime.MetadataWord(70, 9));
        Assert.AreEqual(1, runtime.MetadataWord(80, 9));
    }

    private static byte[] Bytes(IEnumerable<short> values)
    {
        var words = values.ToArray();
        var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), words[index]);
        return bytes;
    }

    private static LegacyArchive Archive(params byte[][] entries)
    {
        var index = new byte[entries.Length * 4];
        var end = 0;
        for (var entry = 0; entry < entries.Length; entry++)
        {
            end += entries[entry].Length;
            BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(entry * 4), end);
        }
        return LegacyArchive.Parse(index, entries.SelectMany(bytes => bytes).ToArray());
    }
}
