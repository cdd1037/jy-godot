using System.Buffers.Binary;
using System.Text.Json;
using JinYong.Content.Legacy;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class WorldTravelRulesTests
{
    private static WorldMapDefinition Map(params (WorldLayer Layer, GridPosition Point, short Value)[] changes)
    {
        var layers = Enumerable.Range(0, WorldMapDefinition.LayerCount).Select(_ => new short[WorldMapDefinition.CellCount]).ToArray();
        Array.Fill(layers[0], (short)4);
        foreach (var (layer, point, value) in changes) layers[(int)layer][point.Y * WorldMapDefinition.Extent + point.X] = value;
        return new(layers);
    }
    private static WorldTravelSnapshot Start(bool ship = false) => new(new(20, 20), Direction.Down, ship,
        ship ? new(20, 20) : new(100, 100), ship ? new(21, 20) : new(101, 100), Direction.Right);
    private static short[] Metadata()
    {
        var words = new short[26]; words[8] = -1;
        for (var field = 10; field <= 13; field++) words[field] = -1;
        for (var field = 16; field <= 25; field++) words[field] = -1;
        return words;
    }
    private static WorldSceneDefinition Scene(int id, short[] metadata) => new(id, $"Scene {id}", metadata,
        MovementTests.EmptyLayers(), Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray());
    private static SceneWorldState World(params WorldSceneDefinition[] scenes) =>
        new(scenes[0].SceneId, new SceneRuntimeState(scenes[0].ToSceneDefinition()), new WorldDefinition(scenes));
    private static PartyState Party(int speed, bool companion = true)
    {
        RoleDefinition Role(int id, int value)
        {
            var words = new int[RoleWords.Count]; words[0] = id; words[RoleWords.Speed] = value;
            return new(id, $"Role {id}", "", words);
        }
        return new(new PartyDefinition([Role(0, 0), Role(1, speed)], companion ? [0, 1, -1, -1, -1, -1] : [0, -1, -1, -1, -1, -1]));
    }

    [TestMethod]
    public void WorldImportRequires480LayersAndPreservesSignedAndUnsignedBits()
    {
        var input = LegacyWorldMapImporter.LayerFiles.ToDictionary(name => name, _ => new byte[WorldMapDefinition.CellCount * 2]);
        BinaryPrimitives.WriteInt16LittleEndian(input["EARTH.002"].AsSpan(2 * (20 * 480 + 21)), 5002);
        BinaryPrimitives.WriteInt16LittleEndian(input["BUILDX.002"].AsSpan(2 * (20 * 480 + 21)), -1);
        var map = LegacyWorldMapImporter.Read(name => input[name]);
        Assert.AreEqual((short)5002, map.At(WorldLayer.Earth, new(21, 20)));
        Assert.AreEqual((short)-1, map.At(WorldLayer.BuildX, new(21, 20)));
        Assert.AreEqual(ushort.MaxValue, map.RawAt(WorldLayer.BuildX, new(21, 20)));
        input["EARTH.002"][2 * (20 * 480 + 21)] = 0;
        Assert.AreEqual((short)5002, map.At(WorldLayer.Earth, new(21, 20)));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyWorldMapImporter.Read(_ => new byte[512 * 512 * 2]));
    }

    [TestMethod]
    [DataRow(358)] [DataRow(362)] [DataRow(374)] [DataRow(380)] [DataRow(458)] [DataRow(464)]
    [DataRow(506)] [DataRow(670)] [DataRow(818)] [DataRow(824)] [DataRow(838)] [DataRow(934)]
    [DataRow(936)] [DataRow(1016)] [DataRow(1022)]
    public void WalkingRejectsOriginalWaterAndBlockedTerrainRanges(int earth)
    {
        var map = Map((WorldLayer.Earth, new(21, 20), (short)earth));
        var move = WorldTravelRules.Probe(map, Start(), Direction.Right);
        Assert.AreEqual(WorldTravelBlock.Terrain, move.Block); Assert.AreEqual(new GridPosition(20, 20), move.State.Position);
        Assert.AreEqual(Direction.Right, move.State.Facing);
    }

    [TestMethod]
    [DataRow(WorldLayer.Building)] [DataRow(WorldLayer.BuildX)] [DataRow(WorldLayer.BuildY)]
    public void WalkingRejectsBuildingsAndNonzeroOwnerCoordinates(WorldLayer layer)
    {
        Assert.AreEqual(WorldTravelBlock.Building, WorldTravelRules.Probe(Map((layer, new(21, 20), -1)), Start(), Direction.Right).Block);
    }

    [TestMethod]
    public void WalkingIgnoresSurfaceAndChecksOnlyTheMovedCoordinateBoundary()
    {
        var map = Map((WorldLayer.Surface, new(21, 20), 5002));
        Assert.IsTrue(WorldTravelRules.Probe(map, Start(), Direction.Right).CanMove);
        var edge = Start() with { Position = new(5, 30) };
        Assert.AreEqual(WorldTravelBlock.Boundary, WorldTravelRules.Probe(map, edge, Direction.Right).Block);
        Assert.IsTrue(WorldTravelRules.Probe(map, edge, Direction.Up).CanMove);
        var outside = Start() with { Position = new(458, 20) };
        Assert.AreEqual(WorldTravelBlock.Boundary, WorldTravelRules.Probe(map, outside, Direction.Right).Block);
    }

    [TestMethod]
    public void EitherShipCellCanBeBoardedBeforeOrdinaryLandCollision()
    {
        var map = Map((WorldLayer.Earth, new(21, 20), 358), (WorldLayer.Building, new(21, 20), 8));
        foreach (var state in new[] { Start() with { ShipPosition = new(21, 20) }, Start() with { ShipBow = new(21, 20) } })
        {
            var boarded = WorldTravelRules.Probe(map, state, Direction.Right);
            Assert.IsTrue(boarded.Boarded); Assert.IsTrue(boarded.State.InShip);
            Assert.AreEqual(new GridPosition(21, 20), boarded.State.Position);
            Assert.AreEqual(new GridPosition(22, state.ShipBow.Y), boarded.State.ShipBow);
        }
    }

    [TestMethod]
    public void SailingUsesBothCellsAndKeepsTheShipOnBlockedTurns()
    {
        var map = Map((WorldLayer.Earth, new(22, 20), 358), (WorldLayer.Building, new(21, 20), 8),
            (WorldLayer.BuildX, new(21, 20), 15));
        var sailing = WorldTravelRules.Probe(map, Start(true), Direction.Right);
        Assert.IsTrue(sailing.CanMove); Assert.IsTrue(sailing.State.InShip);
        Assert.AreEqual(new GridPosition(21, 20), sailing.State.ShipPosition);
        Assert.AreEqual(new GridPosition(22, 20), sailing.State.ShipBow);
        var blockedMap = Map((WorldLayer.Earth, new(20, 19), 358), (WorldLayer.Building, new(20, 19), 8),
            (WorldLayer.Building, new(20, 18), 10));
        var blocked = WorldTravelRules.Probe(blockedMap, Start(true), Direction.Up);
        Assert.AreEqual(WorldTravelBlock.Building, blocked.Block); Assert.IsTrue(blocked.State.InShip);
        Assert.AreEqual(new GridPosition(20, 20), blocked.State.Position);
        Assert.AreEqual(new GridPosition(20, 19), blocked.State.ShipBow); Assert.AreEqual(Direction.Up, blocked.State.ShipFacing);
    }

    [TestMethod]
    public void LandingRequiresClearLandAndSurfaceAndLeavesTheShipBehind()
    {
        var start = Start(true);
        var landed = WorldTravelRules.Probe(Map(), start, Direction.Right);
        Assert.IsTrue(landed.Disembarked); Assert.IsFalse(landed.State.InShip);
        Assert.AreEqual(new GridPosition(21, 20), landed.State.Position);
        Assert.AreEqual(start.ShipPosition, landed.State.ShipPosition); Assert.AreEqual(start.ShipBow, landed.State.ShipBow);
        Assert.AreEqual(WorldTravelBlock.Surface, WorldTravelRules.Probe(Map((WorldLayer.Surface, new(21, 20), 4)), start, Direction.Right).Block);
        Assert.IsFalse(WorldTravelRules.Probe(Map((WorldLayer.Earth, new(21, 20), 956)), start, Direction.Right).CanMove);
        // Original landing uses its own broad 672..954 land range: 838 is
        // blocked for walking but remains a valid disembarkation tile.
        Assert.IsTrue(WorldTravelRules.Probe(Map((WorldLayer.Earth, new(21, 20), 838)), start, Direction.Right).Disembarked);
    }

    [TestMethod]
    [DataRow(0, 0, true)] [DataRow(1, 100, false)] [DataRow(2, 69, false)] [DataRow(2, 70, true)] [DataRow(3, 100, false)]
    public void EntranceConditionsUseEitherCoordinateAndAnActivePartyMembersSpeed(int condition, int speed, bool allowed)
    {
        var metadata = Metadata(); metadata[9] = (short)condition;
        metadata[10] = 20; metadata[11] = 19; metadata[12] = 21; metadata[13] = 20;
        var scenes = World(Scene(0, metadata));
        Assert.AreEqual(allowed ? 0 : (int?)null, WorldTravelRules.FindEntrance(scenes, Party(speed), new(20, 19)));
        Assert.AreEqual(allowed ? 0 : (int?)null, WorldTravelRules.FindEntrance(scenes, Party(speed), new(21, 20)));
        if (condition == 2) Assert.IsNull(WorldTravelRules.FindEntrance(scenes, Party(100, companion: false), new(20, 19)));
    }

    [TestMethod]
    public void EntranceIsCheckedBeforeCollisionAndContinuationCanSuppressReentry()
    {
        var metadata = Metadata(); metadata[10] = 21; metadata[11] = 20;
        var scenes = World(Scene(0, metadata));
        var map = Map((WorldLayer.Building, new(21, 20), 8));
        var travel = new WorldTravelState(Start());
        var entrance = travel.TryMove(map, Direction.Right, scenes);
        Assert.AreEqual(0, entrance.EntranceSceneId); Assert.AreEqual(new GridPosition(20, 20), travel.Snapshot.Position);
        Assert.AreEqual(WorldTravelBlock.Building, travel.TryMove(map, Direction.Right, scenes, detectEntrances: false).Block);
        var restored = JsonSerializer.Deserialize<WorldTravelSnapshot>(JsonSerializer.Serialize(travel.Snapshot))!;
        travel.Restore(restored); Assert.AreEqual(restored, travel.Snapshot);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => travel.Restore(restored with { Position = new(480, 0) }));
    }

    [TestMethod]
    public void SceneExitsTakePriorityAndLinkedScenesChooseTheCorrectReturnEntrance()
    {
        var source = Metadata(); var target = Metadata();
        source[8] = 1; source[22] = 5; source[23] = 6;
        target[14] = 10; target[15] = 11; target[24] = 20; target[25] = 21;
        var scenes = World(Scene(0, source), Scene(1, target));
        var jump = WorldTravelRules.ResolveSceneTransition(scenes, 0, new(5, 6))!;
        Assert.AreEqual(1, jump.SceneId); Assert.AreEqual(new GridPosition(10, 11), jump.Position); Assert.IsFalse(jump.ReturnWorld);
        scenes.SetMetadataWord(0, 10, 0); scenes.SetMetadataWord(0, 11, 0);
        Assert.AreEqual(new GridPosition(20, 21), WorldTravelRules.ResolveSceneTransition(scenes, 0, new(5, 6))!.Position);
        scenes.SetMetadataWord(0, 18, 5); scenes.SetMetadataWord(0, 21, 6);
        Assert.IsTrue(WorldTravelRules.ResolveSceneTransition(scenes, 0, new(5, 6))!.ReturnWorld);
        scenes.SetMetadataWord(0, 18, -1); scenes.SetMetadataWord(0, 8, 83);
        Assert.ThrowsExactly<InvalidOperationException>(() => WorldTravelRules.ResolveSceneTransition(scenes, 0, new(5, 6)));
    }

    [TestMethod]
    public void MissingMetadataDoesNotCreateEntrancesExitsOrJumpSpawns()
    {
        var source = Metadata(); source[8] = 1; source[22] = 5; source[23] = 6;
        var missing = new WorldSceneDefinition(1, "No metadata", new short[26], MovementTests.EmptyLayers(),
            Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray(), hasLegacyMetadata: false);
        var scenes = World(Scene(0, source), missing);
        Assert.IsNull(WorldTravelRules.FindEntrance(scenes, null, new(0, 0)));
        Assert.IsNull(WorldTravelRules.ResolveSceneTransition(scenes, 1, new(0, 0)));
        Assert.ThrowsExactly<InvalidOperationException>(() => WorldTravelRules.SceneEntry(scenes, 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => WorldTravelRules.ResolveSceneTransition(scenes, 0, new(5, 6)));
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualWorldHasFive480LayersAndTheHomeEntranceIsReachableFromItsNeighbour()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to validate original assets.");
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(source!, name));
        var map = LegacyWorldMapImporter.Read(Read);
        var ranger = LegacyArchive.Parse(Read("RANGER.IDX"), Read("RANGER.GRP"));
        var snapshot = LegacyWorldMapImporter.ReadState(ranger.Entry(0));
        Assert.IsTrue(WorldMapDefinition.Contains(snapshot.Position));
        Assert.AreEqual(WorldMapDefinition.CellCount, map.Layer(WorldLayer.Earth).Count);
        var world = LegacyWorldImporter.Read(ranger, LegacyArchive.Parse(Read("ALLSIN.IDX"), Read("ALLSIN.GRP")),
            LegacyArchive.Parse(Read("ALLDEF.IDX"), Read("ALLDEF.GRP")));
        var home = world.Scene(70); var mutable = new SceneWorldState(70, new SceneRuntimeState(home.ToSceneDefinition()), world);
        var entrance = new GridPosition(home.Metadata[10], home.Metadata[11]);
        Assert.AreEqual(70, WorldTravelRules.FindEntrance(mutable, new PartyState(LegacyPartyImporter.Read(ranger)), entrance));
        var from = snapshot with { Position = new(entrance.X, entrance.Y + 1), InShip = false };
        var move = WorldTravelRules.Probe(map, from, Direction.Up, mutable);
        Assert.AreEqual(70, move.EntranceSceneId); Assert.AreEqual(from.Position, move.State.Position);
        Assert.AreEqual(new GridPosition(home.Metadata[14], home.Metadata[15]), WorldTravelRules.SceneEntry(mutable, 70));
    }
}
