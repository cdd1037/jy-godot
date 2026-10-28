using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

/// <summary>Integration of imported-style world operations through the sole C# event runtime.</summary>
[TestClass]
public sealed class LegacyWorldEventIntegrationTests
{
    private static EventCommand C(int id, EventOperation operation, params int[] arguments) => new(id, operation, arguments);

    private static WorldDefinition World(params int[] sceneIds) => new(sceneIds.Distinct().Select(id =>
    {
        var metadata = new short[26];
        metadata[0] = (short)id;
        metadata[9] = 1;
        metadata[14] = 19;
        metadata[15] = 20;
        return new WorldSceneDefinition(id, $"Scene {id}", metadata, MovementTests.EmptyLayers(),
            Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray());
    }).ToArray());

    private static GameSession Session(WorldDefinition world, EventProgram[] programs, PartyDefinition? party = null) =>
        new(world.Scene(70).ToSceneDefinition(), new StoryCatalog(70, null, 0, null, Direction.Right, programs,
            [new(10, ["是"]), new(11, ["否"])], new Dictionary<int, string> { [0] = "银两" },
            new Dictionary<int, int> { [0] = 2 }, party: party, world: world));

    [TestMethod]
    public void RunnerChangesRemoteEventsAndTilesThenBranchesOnTheirUpdatedValues()
    {
        var world = World(70, 3);
        var program = new EventProgram(1,
        [
            C(0, EventOperation.ChangeEvent, 3, 2, 0, 0, 1, -1, -1, 20, 20, 20, 0, 10, 11),
            C(1, EventOperation.IncrementEventScripts, 3, 2, 4, 2, 1),
            C(2, EventOperation.SetTile, 3, (int)MapLayer.Ground, 1, 2, 6),
            C(3, EventOperation.ReplaceTiles, 3, (int)MapLayer.Ground, 6, 14),
            C(4, EventOperation.SetTile, -2, (int)MapLayer.Building, 20, 20, 4),
            C(5, EventOperation.BranchEvent, 3, 2, 2, 5, 6, 8),
            C(6, EventOperation.Dialogue, 10, 0, 0), C(7, EventOperation.End),
            C(8, EventOperation.Dialogue, 11, 0, 0), C(9, EventOperation.End)
        ]);
        var session = Session(world, [program]);
        Assert.IsTrue(session.Events!.TryStart(1, 2, session.Player.Position));
        Assert.IsNull(session.Events.Error);
        Assert.AreSame(session.Scene, session.World.Scene(70));
        Assert.AreEqual(10, session.Events.Pending!.DialogueId);
        Assert.AreEqual(5, session.World.EventField(3, 2, 2));
        Assert.AreEqual(1, session.World.EventField(3, 2, 3));
        Assert.AreEqual(0, session.World.EventField(3, 2, 4));
        Assert.AreEqual(2, session.World.Tile(3, MapLayer.Event, new(10, 11)));
        Assert.AreEqual(14, session.World.Tile(3, MapLayer.Ground, new(1, 2)));
        Assert.AreEqual(4, session.Scene.At(MapLayer.Building, new(20, 20)));
        Assert.AreEqual(0, world.Scene(3).ToSceneDefinition().Events[2][2]);
        Assert.AreEqual(0, session.Definition.At(MapLayer.Building, new(20, 20)));
        Assert.IsTrue(session.Events.Confirm(session.Events.Pending.Token));
        Assert.AreEqual(MovementBlock.Building, session.TryMove(Direction.Right).Block);
    }

    [TestMethod]
    [DataRow(EventOperation.ChangeEvent)]
    [DataRow(EventOperation.SetTile)]
    [DataRow(EventOperation.BranchEvent)]
    [DataRow(EventOperation.IncrementEventScripts)]
    [DataRow(EventOperation.ReplaceTiles)]
    [DataRow(EventOperation.OpenScene)]
    public void RemoteTargetPreflightFailsBeforeAnyCallerInventoryOrWorldChanges(EventOperation operation)
    {
        var root = new EventProgram(1, [C(0, EventOperation.AdjustItem, 0, 100), C(1, EventOperation.OpenScene, 3),
            C(2, EventOperation.Call, 2), C(3, EventOperation.End)]);
        var missing = operation switch
        {
            EventOperation.ChangeEvent => C(0, operation, 99, 2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2),
            EventOperation.SetTile => C(0, operation, 99, (int)MapLayer.Ground, 1, 2, 6),
            EventOperation.BranchEvent => C(0, operation, 99, 2, 2, 0, 1, 1),
            EventOperation.IncrementEventScripts => C(0, operation, 99, 2, 1, 1, 1),
            EventOperation.ReplaceTiles => C(0, operation, 99, (int)MapLayer.Ground, 0, 6),
            EventOperation.OpenScene => C(0, operation, 99),
            _ => throw new InvalidOperationException()
        };
        var child = new EventProgram(2, [missing, C(1, EventOperation.End)]);
        var session = Session(World(70, 3), [root, child]);
        Assert.IsFalse(session.Events!.TryStart(1, 0, session.Player.Position));
        Assert.IsNotNull(session.Events.Error);
        StringAssert.Contains(session.Events.Error, "Program 2, command 0");
        Assert.AreEqual(2, session.Inventory[0]);
        Assert.AreEqual(1, session.World.MetadataWord(3, 9));
        Assert.AreEqual(0L, session.Scene.Revision);
        Assert.AreEqual(0L, session.World.Scene(3).Revision);
        Assert.AreEqual(0, session.Events.Trace.Count);
        Assert.IsFalse(session.IsEventRunning);
    }

    [TestMethod]
    public void EntranceCommandsExecuteThroughRunnerWithLegacyExceptions()
    {
        var program = new EventProgram(1,
            [C(0, EventOperation.OpenScene, 2), C(1, EventOperation.Wait, 1),
                C(2, EventOperation.OpenAllScenes), C(3, EventOperation.End)]);
        var session = Session(World(70, 2, 38, 75, 80), [program]);
        Assert.IsTrue(session.Events!.TryStart(1, 0, session.Player.Position));
        Assert.AreEqual(0, session.World.MetadataWord(2, 9));
        Assert.AreEqual(1, session.World.MetadataWord(70, 9));
        session.Events.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(0, session.World.MetadataWord(70, 9));
        Assert.AreEqual(2, session.World.MetadataWord(2, 9));
        Assert.AreEqual(2, session.World.MetadataWord(38, 9));
        Assert.AreEqual(1, session.World.MetadataWord(75, 9));
        Assert.AreEqual(1, session.World.MetadataWord(80, 9));
    }

    [TestMethod]
    [DataRow(true, 10)]
    [DataRow(false, 11)]
    public void BookBranchReadsFourteenEventPicturesAfterEarlierScriptChanges(bool complete, int expectedDialogue)
    {
        var commands = Enumerable.Range(11, complete ? 14 : 13).Select((index, commandId) => C(commandId,
            EventOperation.ChangeEvent, -2, index, -2, -2, -2, -2, -2, 4664, 4664, 4664, -2, -2, -2)).ToList();
        commands.AddRange([C(20, EventOperation.BranchBooks, 30, 40),
            C(30, EventOperation.Dialogue, 10, 0, 0), C(31, EventOperation.End),
            C(40, EventOperation.Dialogue, 11, 0, 0), C(41, EventOperation.End)]);
        var session = Session(World(70), [new EventProgram(1, commands)]);
        Assert.IsTrue(session.Events!.TryStart(1, 0, session.Player.Position));
        Assert.IsNull(session.Events.Error);
        Assert.AreEqual(expectedDialogue, session.Events.Pending!.DialogueId);
    }

    [TestMethod]
    public void MoveShopUsesExistingEventRandomStateAndWritesRemoteInn()
    {
        // Three random dialogues advance the shared seed before opcode 65. The fourth
        // original LCG draw is 17515, whose remainder modulo five selects scene 1.
        var program = new EventProgram(1, [C(0, EventOperation.RandomDialogue, 10, 2, 0, 0),
            C(1, EventOperation.RandomDialogue, 10, 2, 0, 0), C(2, EventOperation.RandomDialogue, 10, 2, 0, 0),
            C(3, EventOperation.MoveShop), C(4, EventOperation.End)]);
        var session = Session(World(70, 1, 3, 40, 60, 61), [program]);
        Assert.IsTrue(session.Events!.TryStart(1, 0, session.Player.Position));
        for (var index = 0; index < 3; index++)
        {
            Assert.AreEqual(EventPresentationKind.Dialogue, session.Events.Pending!.Kind);
            Assert.IsTrue(session.Events.Confirm(session.Events.Pending.Token));
        }
        Assert.IsNull(session.Events.Error);
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(938, session.World.EventField(1, 16, 2));
        Assert.AreEqual(8256, session.World.EventField(1, 16, 7));
        Assert.AreEqual(0, session.World.EventField(60, 16, 2));
        Assert.AreSame(session.Scene, session.World.Scene());
    }

    [TestMethod]
    public void DismissPartyUpdatesCompanionsAndTheirRemoteMapEventsTogether()
    {
        RoleDefinition Role(int id)
        {
            var words = new int[RoleWords.Count];
            words[RoleWords.Id] = id;
            for (var slot = 0; slot < RoleWords.TakingItemCount; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
            return new(id, $"Role {id}", "", words);
        }
        var party = new PartyDefinition([Role(0), Role(1), Role(2)], [0, 1, 2, -1, -1, -1]);
        var program = new EventProgram(1, [C(0, EventOperation.DismissParty), C(1, EventOperation.End)]);
        var session = Session(World(Enumerable.Range(0, 100).ToArray()), [program], party);
        Assert.IsTrue(session.Events!.TryStart(1, 0, session.Player.Position));
        Assert.IsNull(session.Events.Error);
        CollectionAssert.AreEqual(new[] { 0, -1, -1, -1, -1, -1 }, session.Party!.Members.ToArray());
        Assert.AreEqual(-1, session.World.EventField(44, 1, 7));
        Assert.AreEqual(-1, session.World.EventField(80, 1, 2));
        Assert.AreEqual(0, session.World.EventField(70, 2, 2));
        Assert.IsFalse(session.IsEventRunning);
    }
}
