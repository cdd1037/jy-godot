using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class GameTravelIntegrationTests
{
    private static readonly GridPosition Spawn = new(10, 10), Exit = new(11, 10);
    private static short[] Metadata(int id)
    {
        var words = new short[26]; words[0] = (short)id; words[8] = -1;
        words[10] = (short)(21 + id * 10); words[11] = (short)(20 + id * 10);
        words[12] = words[13] = -1;
        words[14] = words[15] = (short)(10 + id * 5);
        for (var field = 16; field <= 25; field++) words[field] = -1;
        words[24] = words[25] = 25;
        return words;
    }
    private static GameSession Session(bool map = true, bool jump = false, bool returnEntrance = false, bool autoEvent = false,
        bool eventMovesAway = false)
    {
        var first = Metadata(0); var second = Metadata(1);
        if (jump)
        {
            first[8] = 1; first[22] = (short)Exit.X; first[23] = (short)Exit.Y;
            if (returnEntrance) first[10] = first[11] = 0;
        }
        else { first[16] = (short)Exit.X; first[19] = (short)Exit.Y; }
        var layers = MovementTests.EmptyLayers();
        var points = Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray();
        foreach (var point in points) point[9] = point[10] = -1;
        if (autoEvent)
        {
            points[0][4] = 10; points[0][9] = (short)Exit.X; points[0][10] = (short)Exit.Y;
            layers[(int)MapLayer.Event * SceneDefinition.TileCount + Exit.Y * 64 + Exit.X] = 0;
        }
        var a = new WorldSceneDefinition(0, "First", first, layers, points);
        var b = new WorldSceneDefinition(1, "Second", second, MovementTests.EmptyLayers(),
            Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray());
        var role = new int[RoleWords.Count]; role[RoleWords.Hp] = role[RoleWords.MaximumHp] = 100;
        for (var slot = 0; slot < 4; slot++) role[RoleWords.TakingItemBegin + slot] = -1;
        var party = new PartyDefinition([new RoleDefinition(0, "Hero", "", role)], [0, -1, -1, -1, -1, -1]);
        EventProgram[] programs = [];
        if (autoEvent)
        {
            var commands = new List<EventCommand> { new(0, EventOperation.Dialogue, [10, 0, 0]) };
            if (eventMovesAway) commands.Add(new(1, EventOperation.Teleport, [Spawn.X, Spawn.Y]));
            commands.Add(new(2, EventOperation.End, []));
            programs = [new EventProgram(10, commands)];
        }
        var story = new StoryCatalog(0, null, 0, null, Direction.Right, programs, [new DialogueDefinition(10, ["Before leaving"] )],
            new Dictionary<int, string> { [0] = "Item" }, new Dictionary<int, int>(),
            variableNames: new Dictionary<int, string> { [0] = "test:travel/state" }, party: party, world: new WorldDefinition([a, b]));
        var worldLayers = Enumerable.Range(0, 5).Select(_ => new short[WorldMapDefinition.CellCount]).ToArray();
        Array.Fill(worldLayers[0], (short)4);
        var travel = new WorldTravelSnapshot(new(20, 20), Direction.Right, false, new(100, 100), new(101, 100), Direction.Right);
        return new GameSession(a.ToSceneDefinition(Spawn), story, worldMap: map ? new WorldMapDefinition(worldLayers) : null,
            travelStart: map ? travel : null);
    }
    private static void WalkToExit(GameSession session)
    {
        Assert.IsTrue(session.TryMove(Direction.Right).Moved);
        session.CompleteMovement();
    }
    private static void CommitAndFinishFade(GameSession session)
    {
        var token = session.PendingTravel!.Token;
        session.Advance(TimeSpan.FromMilliseconds(450));
        Assert.IsTrue(session.IsTravelReady); Assert.IsTrue(session.CommitTravel(token));
        Assert.IsFalse(session.CommitTravel(token));
        Assert.IsTrue(session.IsInputLocked);
        session.Advance(TimeSpan.FromMilliseconds(450));
        Assert.IsFalse(session.IsInputLocked);
    }

    [TestMethod]
    public void ExitWaitsForFadeAndExplicitCommitThenEntersWorldWithTheSameSession()
    {
        var session = Session(); WalkToExit(session);
        var request = session.PendingTravel!;
        Assert.IsTrue(request.IsWorld); Assert.IsFalse(session.IsWorld); Assert.IsTrue(session.IsInputLocked);
        Assert.IsFalse(session.CommitTravel(request.Token));
        session.Advance(TimeSpan.FromMilliseconds(449));
        Assert.IsFalse(session.IsTravelReady); Assert.IsFalse(session.CommitTravel(request.Token));
        session.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(session.CommitTravel(request.Token)); Assert.IsTrue(session.IsWorld);
        Assert.AreEqual(new GridPosition(20, 20), session.Player.Position);
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Right).Block);
        session.Advance(TimeSpan.FromMilliseconds(450));
        Assert.IsFalse(session.IsInputLocked); Assert.IsNull(session.PendingTravel);
    }

    [TestMethod]
    public void WorldRoundTripRetainsInventoryPartyVariablesEventsAndMapChanges()
    {
        var session = Session(); var events = session.Events; var party = session.Party; var world = session.World;
        ((IEventWorld)session).AddItem(0, 2); ((IEventWorld)session).SetVariable(0, 9);
        party!.SetWord(0, RoleWords.Hp, 77);
        world.SetTile(0, MapLayer.Building, new(12, 12), 100);
        world.SetEventField(0, 5, 2, 123);
        WalkToExit(session); CommitAndFinishFade(session);
        Assert.IsTrue(session.IsWorld);
        session.TryMove(Direction.Right);
        Assert.AreEqual(0, session.PendingTravel!.SceneId); Assert.IsFalse(session.PendingTravel.IsWorld);
        CommitAndFinishFade(session);
        Assert.IsFalse(session.IsWorld); Assert.AreEqual(Spawn, session.Player.Position);
        Assert.AreSame(events, session.Events); Assert.AreSame(party, session.Party); Assert.AreSame(world, session.World);
        Assert.AreEqual(2, session.Inventory[0]); Assert.AreEqual(9, session.Variables["test:travel/state"]);
        Assert.AreEqual(77, session.Party!.GetWord(0, RoleWords.Hp));
        Assert.AreEqual((short)100, session.Scene.At(MapLayer.Building, new(12, 12)));
        Assert.AreEqual((short)123, session.Scene.Events[5][2]);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void LinkedSceneTravelUsesNormalOrJumpReturnEntryWithoutResettingGlobalState(bool returnEntrance)
    {
        var session = Session(jump: true, returnEntrance: returnEntrance);
        ((IEventWorld)session).AddItem(0, 3);
        WalkToExit(session);
        Assert.AreEqual(1, session.PendingTravel!.SceneId); Assert.IsFalse(session.PendingTravel.IsWorld);
        Assert.AreEqual(returnEntrance ? new GridPosition(25, 25) : new GridPosition(15, 15), session.PendingTravel.Position);
        CommitAndFinishFade(session);
        Assert.IsFalse(session.IsWorld); Assert.AreEqual(1, session.World.CurrentSceneId);
        Assert.AreEqual(returnEntrance ? new GridPosition(25, 25) : new GridPosition(15, 15), session.Player.Position);
        Assert.AreEqual(3, session.Inventory[0]);
    }

    [TestMethod]
    public void ArrivalEventFinishesBeforeExitFadeStarts()
    {
        var session = Session(autoEvent: true); WalkToExit(session);
        Assert.AreEqual(10, session.Events!.Pending!.DialogueId);
        Assert.IsNull(session.PendingTravel); Assert.IsTrue(session.IsEventRunning);
        session.Advance(TimeSpan.FromSeconds(1)); Assert.IsNull(session.PendingTravel);
        Assert.IsTrue(session.Events.Confirm(session.Events.Pending.Token));
        session.Advance(TimeSpan.Zero);
        Assert.IsFalse(session.IsEventRunning); Assert.IsNotNull(session.PendingTravel);
        Assert.IsFalse(session.IsTravelReady); Assert.AreEqual(1, session.Events.CompletedCount);
        CommitAndFinishFade(session); Assert.IsTrue(session.IsWorld);
    }

    [TestMethod]
    public void ArrivalEventCanMovePlayerAwayAndCancelTheExit()
    {
        var session = Session(autoEvent: true, eventMovesAway: true); WalkToExit(session);
        session.Events!.Confirm(session.Events.Pending!.Token); session.Advance(TimeSpan.Zero);
        Assert.AreEqual(Spawn, session.Player.Position); Assert.IsNull(session.PendingTravel);
        Assert.IsFalse(session.IsInputLocked); Assert.IsFalse(session.IsWorld);
    }

    [TestMethod]
    public void RejectingOrCompletingTravelMakesItsTokenStale()
    {
        var session = Session(); WalkToExit(session);
        var old = session.PendingTravel!.Token;
        session.RejectTravel(old, "Target view is not available.");
        Assert.IsNull(session.PendingTravel); Assert.IsFalse(session.IsWorld); Assert.IsFalse(session.CommitTravel(old));
        session.Advance(TimeSpan.FromMilliseconds(450)); // Rejection restores the existing view through the same fade-in.
        Assert.IsTrue(session.TryMove(Direction.Left).Moved); session.CompleteMovement();
        WalkToExit(session);
        Assert.AreNotEqual(old, session.PendingTravel!.Token);
        session.Advance(TimeSpan.FromMilliseconds(450));
        Assert.IsFalse(session.CommitTravel(old));
        Assert.IsTrue(session.CommitTravel(session.PendingTravel.Token));
    }

    [TestMethod]
    public void MissingWorldMapDoesNotCreateAFakeWorldOrLockAnOldSceneView()
    {
        var session = Session(map: false); WalkToExit(session);
        session.Advance(TimeSpan.FromSeconds(1));
        Assert.IsFalse(session.IsWorld); Assert.IsNull(session.MainMap); Assert.IsNull(session.Travel);
        Assert.IsNull(session.PendingTravel); Assert.IsFalse(session.IsInputLocked);
        Assert.IsTrue(session.TryMove(Direction.Left).Moved);
    }

    [TestMethod]
    public void DirectionHeldAcrossTravelMustBeReleasedBeforeMovingInTheNewView()
    {
        var session = Session(); var walker = new SceneWalker(session);
        Assert.IsTrue(walker.ReadInput([Direction.Right], true)!.Value.Moved);
        walker.Advance(0.06);
        Assert.IsNotNull(session.PendingTravel);
        Assert.IsNull(walker.ReadInput([Direction.Right], true));
        CommitAndFinishFade(session); walker.Advance(0);
        Assert.IsNull(walker.ReadInput([Direction.Right], true));
        Assert.IsNull(session.PendingTravel);
        walker.ReadInput([], true); walker.Advance(0);
        Assert.IsNotNull(walker.ReadInput([Direction.Right], true));
        Assert.IsNotNull(session.PendingTravel); // Fresh press now reaches the scene entrance.
    }
}
