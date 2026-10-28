using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class MovementTests
{
    internal static SceneDefinition Scene(short[]? layers = null, SceneEventDefinition[]? events = null, GridPosition? spawn = null)
    {
        layers ??= EmptyLayers();
        events ??= Enumerable.Range(0, 200).Select(_ => new SceneEventDefinition(new short[11])).ToArray();
        return new SceneDefinition("test:scene/walk", "Walk", spawn ?? new(19, 20), layers, events);
    }

    internal static short[] EmptyLayers()
    {
        var layers = new short[6 * 4096];
        Array.Fill(layers, (short)-1, 3 * 4096, 4096);
        return layers;
    }

    private static int Cell(MapLayer layer, int x = 20, int y = 20) => (int)layer * 4096 + y * 64 + x;

    [TestMethod]
    public void MoveCommitsPositionAndBlockedAttemptOnlyChangesFacing()
    {
        var layers = EmptyLayers();
        layers[Cell(MapLayer.Building, 21)] = 4;
        var session = new GameSession(Scene(layers));
        Assert.IsTrue(session.TryMove(Direction.Right).Moved);
        Assert.AreEqual(new GridPosition(20, 20), session.Player.Position);
        var blocked = session.TryMove(Direction.Right);
        Assert.AreEqual(MovementBlock.Building, blocked.Block);
        Assert.AreEqual(new GridPosition(20, 20), session.Player.Position);
        Assert.AreEqual(1L, session.Player.Steps);
        Assert.AreEqual(Direction.Right, session.Player.Facing);
    }

    [TestMethod]
    public void SceneOwnsCopiesOfDefinitionArrays()
    {
        var layers = EmptyLayers();
        var scene = Scene(layers);
        layers[Cell(MapLayer.Building)] = 4;
        Assert.AreEqual(MovementBlock.None, SceneMovementRules.Check(scene, new(19, 20), new(20, 20)));
    }

    [TestMethod]
    public void BoundariesAreRejectedWithoutMoving()
    {
        var session = new GameSession(Scene(spawn: new(0, 0)));
        Assert.AreEqual(MovementBlock.Boundary, session.TryMove(Direction.Up).Block);
        Assert.AreEqual(MovementBlock.Boundary, session.TryMove(Direction.Left).Block);
        Assert.AreEqual(new GridPosition(0, 0), session.Player.Position);
    }

    [TestMethod]
    public void HeightThresholdIsDirectionalAndExclusiveAtTen()
    {
        var layers = EmptyLayers();
        layers[Cell(MapLayer.BuildingHeight)] = 9;
        Assert.AreEqual(MovementBlock.None, SceneMovementRules.Check(Scene(layers), new(19, 20), new(20, 20)));
        layers[Cell(MapLayer.BuildingHeight)] = 10;
        var scene = Scene(layers);
        Assert.AreEqual(MovementBlock.Height, SceneMovementRules.Check(scene, new(19, 20), new(20, 20)));
        Assert.AreEqual(MovementBlock.None, SceneMovementRules.Check(scene, new(20, 20), new(19, 20)));
    }

    [TestMethod]
    public void EventZeroCanBlockAndMinusOneMeansNoEvent()
    {
        var layers = EmptyLayers();
        var words = new short[11];
        words[0] = 1;
        var events = Enumerable.Range(0, 200).Select(_ => new SceneEventDefinition(words)).ToArray();
        Assert.AreEqual(MovementBlock.None, SceneMovementRules.Check(Scene(layers, events), new(19, 20), new(20, 20)));
        layers[Cell(MapLayer.Event)] = 0;
        Assert.AreEqual(MovementBlock.Event, SceneMovementRules.Check(Scene(layers, events), new(19, 20), new(20, 20)));
    }

    [TestMethod]
    [DataRow(358)] [DataRow(362)] [DataRow(374)] [DataRow(380)]
    [DataRow(458)] [DataRow(464)] [DataRow(506)] [DataRow(610)]
    [DataRow(818)] [DataRow(824)] [DataRow(838)] [DataRow(934)]
    [DataRow(936)] [DataRow(1016)] [DataRow(1022)] [DataRow(1324)] [DataRow(1348)]
    public void OriginalBlockedTerrainEndpointsArePreserved(int value)
    {
        var layers = EmptyLayers();
        layers[Cell(MapLayer.Ground)] = (short)value;
        Assert.AreEqual(MovementBlock.Terrain, SceneMovementRules.Check(Scene(layers), new(19, 20), new(20, 20)));
    }

    [TestMethod]
    public void TerrainOutsideRangesRemainsWalkable()
    {
        foreach (var value in new short[] { 356, 364, 372, 382, 504, 612, 836, 840, 1346 + 4 })
        {
            var layers = EmptyLayers();
            layers[Cell(MapLayer.Ground)] = value;
            Assert.AreEqual(MovementBlock.None, SceneMovementRules.Check(Scene(layers), new(19, 20), new(20, 20)), $"ground={value}");
        }
    }

    [TestMethod]
    public void ProjectionPreservesLegacyAxesAndElevation()
    {
        Assert.AreEqual((18, 9), IsometricProjection.Project(new(1, 0)));
        Assert.AreEqual((-18, 9), IsometricProjection.Project(new(0, 1)));
        Assert.AreEqual((0, 8), IsometricProjection.Project(new(1, 1), 10));
    }

    [TestMethod]
    public void InvalidEventReferencesAndBlockedSpawnAreRejected()
    {
        var layers = EmptyLayers();
        layers[Cell(MapLayer.Event)] = 200;
        Assert.ThrowsExactly<ArgumentException>(() => Scene(layers));
        layers = EmptyLayers();
        layers[Cell(MapLayer.Building, 19)] = 4;
        Assert.ThrowsExactly<ArgumentException>(() => new GameSession(Scene(layers)));
    }
}
