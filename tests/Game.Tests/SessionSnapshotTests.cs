using System.Text.Json;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Persistence;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class SessionSnapshotTests
{
    private static EventCommand C(int id, EventOperation op, params int[] args) => new(id, op, args);
    private static RoleDefinition Role(int id)
    {
        var words = new int[RoleWords.Count]; words[0] = id;
        words[RoleWords.Hp] = words[RoleWords.MaximumHp] = words[RoleWords.Mp] = words[RoleWords.MaximumMp] = 100;
        words[RoleWords.PhysicalPower] = 100; words[RoleWords.Speed] = 60;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        return new(id, $"Role {id}", "", words);
    }
    private static WorldSceneDefinition Scene(int id, bool exits)
    {
        var metadata = new short[26]; metadata[8] = -1;
        metadata[10] = metadata[11] = 50;
        if (id == 1) { metadata[10] = 21; metadata[11] = 20; }
        metadata[12] = metadata[13] = -1; metadata[14] = metadata[15] = (short)(id == 0 ? 10 : 15);
        for (var word = 16; word < 26; word++) metadata[word] = -1;
        if (exits && id == 0) { metadata[16] = 11; metadata[19] = 10; }
        var points = Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray();
        foreach (var point in points) point[9] = point[10] = -1;
        return new(id, $"Scene {id}", metadata, MovementTests.EmptyLayers(), points);
    }
    private static GameSession Session(bool travel = false)
    {
        var world = new WorldDefinition([Scene(0, travel), Scene(1, travel)]);
        var party = new PartyDefinition(Enumerable.Range(0, 320).Select(Role).ToArray(), [0, -1, -1, -1, -1, -1],
            new Dictionary<int, int> { [2] = 1 });
        var programs = new[]
        {
            new EventProgram(1, [C(0, EventOperation.AdjustItem, 0, 1), C(1, EventOperation.End)]),
            new EventProgram(2, [C(0, EventOperation.RandomDialogue, 10, 3, 0, 0), C(1, EventOperation.End)]),
            new EventProgram(3, [C(0, EventOperation.Wait, 100), C(1, EventOperation.End)])
        };
        var story = new StoryCatalog(0, 1, 0, null, Direction.Down, programs,
            Enumerable.Range(10, 3).Select(id => new DialogueDefinition(id, [$"Dialogue {id}"])),
            new Dictionary<int, string> { [0] = "Item", [2] = "Zero quantity legacy slot" }, new Dictionary<int, int> { [0] = 2 },
            variableNames: new Dictionary<int, string> { [0] = "test:save/quest" }, party: party, world: world,
            initialItemIds: [0, 2]);
        var layers = Enumerable.Range(0, 5).Select(_ => new short[WorldMapDefinition.CellCount]).ToArray();
        Array.Fill(layers[0], (short)4);
        var map = travel ? new WorldMapDefinition(layers) : null;
        var start = travel ? new WorldTravelSnapshot(new(20, 20), Direction.Right, false, new(100, 100), new(101, 100), Direction.Right) : null;
        return new(world.Scene(0).ToSceneDefinition(new(10, 10)), story, worldMap: map, travelStart: start);
    }
    private static SessionSnapshot JsonCopy(SessionSnapshot snapshot) =>
        JsonSerializer.Deserialize<SessionSnapshot>(JsonSerializer.Serialize(snapshot))!;
    private static GameSession Restore(GameSession source, SessionSnapshot snapshot) =>
        GameSession.Restore(source.Definition, source.Story, snapshot, source.MainMap, source.BattleResources);
    private static void CommitTravel(GameSession session)
    {
        var token = session.PendingTravel!.Token; session.Advance(GameSession.TravelFadeTime);
        Assert.IsTrue(session.CommitTravel(token)); session.Advance(GameSession.TravelFadeTime);
        Assert.IsTrue(session.CanSave);
    }

    [TestMethod]
    public void JsonRoundTripPreservesAllRolesWorldChangesAndStablePresentationWithoutReplayingOpening()
    {
        var source = Session(); var world = source.World; var party = source.Party!;
        Assert.AreEqual(3, source.Inventory[0]); Assert.AreEqual(1, source.Events!.CompletedCount);
        Assert.IsTrue(party.Join(1).Joined); party.SetWord(319, RoleWords.Hp, 77);
        party.SetWord(0, RoleWords.Experience, 60000); party.SetWord(319, RoleWords.MagicLevelBegin, 65535);
        ((IEventWorld)source).SetVariable(0, 9);
        Assert.IsTrue(source.TryMove(Direction.Right).Moved); source.CompleteMovement();
        ((IEventWorld)source).Face(Direction.Left); ((IEventWorld)source).SetPictureWrapped(-1, 72);
        ((IEventWorld)source).SetCameraAxis(true, 30); ((IEventWorld)source).SetCameraAxis(false, 40);
        ((IEventWorld)source).SetFade(0.25);
        world.SetTile(0, MapLayer.Building, source.Player.Position, 100); // Valid saved position can become blocked.
        world.SetTile(1, MapLayer.Decoration, new(12, 12), 200);
        world.SetEventField(1, 5, 2, 2); world.SetMetadataWord(1, 9, 2);
        var snapshot = JsonCopy(source.CaptureSnapshot()); var restored = Restore(source, snapshot);
        Assert.AreNotSame(source, restored); Assert.AreNotSame(party, restored.Party); Assert.AreNotSame(world, restored.World);
        Assert.AreEqual(320, restored.Party!.Roles.Count); Assert.AreEqual(60000, restored.Party.GetWord(0, RoleWords.Experience));
        Assert.AreEqual(65535, restored.Party.GetWord(319, RoleWords.MagicLevelBegin));
        Assert.AreEqual(77, restored.Party.GetWord(319, RoleWords.Hp));
        CollectionAssert.AreEqual(party.Members.ToArray(), restored.Party.Members.ToArray());
        Assert.AreEqual(1, restored.Party.ItemUsers[2]);
        Assert.AreEqual(3, restored.Inventory[0]); Assert.IsFalse(restored.Inventory.ContainsKey(2));
        Assert.IsTrue(((IEventWorld)restored).ItemExists(2)); Assert.AreEqual(9, restored.Variables["test:save/quest"]);
        Assert.AreEqual(source.Player, restored.Player); Assert.AreEqual(source.Camera, restored.Camera); Assert.AreEqual(0.25, restored.FadeOpacity);
        Assert.AreEqual((short)100, restored.Scene.At(MapLayer.Building, restored.Player.Position));
        Assert.AreEqual(200, restored.World.Tile(1, MapLayer.Decoration, new(12, 12)));
        Assert.AreEqual(2, restored.World.EventField(1, 5, 2)); Assert.AreEqual(2, restored.World.MetadataWord(1, 9));
        Assert.AreEqual(1, restored.Events!.CompletedCount); Assert.IsTrue(restored.CanSave);
        // Exported arrays/dictionaries and the restored candidate cannot mutate the original.
        snapshot.Party!.Roles[319][RoleWords.Hp] = 1; snapshot.Inventory[0] = 999;
        restored.Party.SetWord(319, RoleWords.Hp, 2); restored.World.SetMetadataWord(1, 9, 0);
        Assert.AreEqual(77, party.GetWord(319, RoleWords.Hp)); Assert.AreEqual(3, source.Inventory[0]); Assert.AreEqual(2, world.MetadataWord(1, 9));
    }

    [TestMethod]
    public void CompletedProgramsRandomStreamAndMonotonicConfirmationTokensResumeExactly()
    {
        var source = Session(); var events = source.Events!;
        Assert.IsTrue(events.TryStart(1, 0, source.Player.Position)); // Preserve duplicate completion entries.
        Assert.IsTrue(events.TryStart(2, 0, source.Player.Position)); var previousToken = events.Pending!.Token;
        Assert.IsTrue(events.Confirm(previousToken));
        var snapshot = source.CaptureSnapshot(); var restored = Restore(source, JsonCopy(snapshot));
        CollectionAssert.AreEqual(new[] { 1, 1, 2 }, restored.Events!.CompletedPrograms.ToArray());
        Assert.IsTrue(events.TryStart(2, 0, source.Player.Position));
        Assert.IsTrue(restored.Events.TryStart(2, 0, restored.Player.Position));
        Assert.AreEqual(events.Pending!.DialogueId, restored.Events.Pending!.DialogueId);
        Assert.AreEqual(events.Pending.Token, restored.Events.Pending.Token); Assert.IsTrue(restored.Events.Pending.Token > previousToken);
        Assert.IsFalse(restored.Events.Confirm(previousToken)); Assert.IsTrue(restored.Events.Confirm(restored.Events.Pending.Token));
        Assert.IsTrue(events.Confirm(events.Pending.Token));
        var expected = source.CaptureSnapshot().Events!; var actual = restored.CaptureSnapshot().Events!;
        Assert.AreEqual(expected.RandomState, actual.RandomState); Assert.AreEqual(expected.Token, actual.Token);
        CollectionAssert.AreEqual(expected.CompletedPrograms.ToArray(), actual.CompletedPrograms.ToArray());
    }

    [TestMethod]
    public void WorldAndSceneTravelRestoreTheCurrentSceneInsteadOfTheStoryOrigin()
    {
        var source = Session(travel: true);
        Assert.IsTrue(source.TryMove(Direction.Right).Moved); source.CompleteMovement();
        Assert.IsFalse(source.CanSave); CommitTravel(source); Assert.IsTrue(source.IsWorld);
        var ship = source.Travel!.Snapshot with { InShip = true, Position = new(100, 100), ShipPosition = new(100, 100),
            ShipBow = new(101, 100), ShipFacing = Direction.Down, Facing = Direction.Left };
        source.Travel.Restore(ship);
        var worldSnapshot = source.CaptureSnapshot(); var worldRestored = Restore(source, JsonCopy(worldSnapshot));
        Assert.IsTrue(worldRestored.IsWorld); Assert.AreEqual(ship, worldRestored.Travel!.Snapshot);
        Assert.AreEqual(source.Player, worldRestored.Player); Assert.IsNull(worldSnapshot.LastWorldEntryDirection);
        source.Travel.Restore(ship with { InShip = false, Position = new(20, 20) });
        source.TryMove(Direction.Right); CommitTravel(source);
        Assert.AreEqual(1, source.World.CurrentSceneId); Assert.AreEqual(0, source.Story!.SceneId);
        source.World.SetEventField(0, 6, 2, 2); source.World.SetMetadataWord(1, 9, 2);
        var snapshot = JsonCopy(source.CaptureSnapshot()); var restored = Restore(source, snapshot);
        Assert.AreEqual(Direction.Right, snapshot.LastWorldEntryDirection);
        Assert.AreEqual(1, restored.World.CurrentSceneId); Assert.AreEqual(source.Definition.Id, restored.Definition.Id);
        Assert.AreEqual(source.Player, restored.Player); Assert.AreEqual(0, restored.Story!.SceneId);
        Assert.AreEqual(2, restored.World.EventField(0, 6, 2)); Assert.AreEqual(2, restored.World.MetadataWord(1, 9));
        Assert.AreEqual(source.CaptureSnapshot().TravelToken, restored.CaptureSnapshot().TravelToken);
        Assert.ThrowsExactly<ArgumentException>(() => GameSession.Restore(source.Story.World!.Scene(0).ToSceneDefinition(), source.Story, snapshot, source.MainMap));
    }

    [TestMethod]
    public void SavingRejectsUnfinishedMovementActiveEventsAndRetiredSessions()
    {
        var source = Session(); Assert.IsTrue(source.CanSave);
        source.TryMove(Direction.Right); Assert.IsFalse(source.CanSave);
        Assert.ThrowsExactly<InvalidOperationException>(() => source.CaptureSnapshot()); source.CompleteMovement();
        Assert.IsTrue(source.Events!.TryStart(2, 0, source.Player.Position)); Assert.IsFalse(source.CanSave);
        Assert.ThrowsExactly<InvalidOperationException>(() => source.CaptureSnapshot());
        source.Events.Confirm(source.Events.Pending!.Token); Assert.IsTrue(source.CanSave);
        Assert.IsTrue(source.Events.TryStart(3, 0, source.Player.Position)); Assert.IsFalse(source.CanSave);
        source.Advance(TimeSpan.FromMilliseconds(100)); Assert.IsTrue(source.CanSave);
        source.RetireForReplacement(); Assert.IsFalse(source.CanSave);
        Assert.ThrowsExactly<InvalidOperationException>(() => source.CaptureSnapshot());
    }

    [TestMethod]
    public void PureMovementPackageCanSaveWithoutInventingStoryOrPartyServices()
    {
        var source = new GameSession(MovementTests.Scene(), sceneIdOverride: 37); source.TryMove(Direction.Left); source.CompleteMovement();
        var restored = Restore(source, JsonCopy(source.CaptureSnapshot()));
        Assert.AreEqual(source.Player, restored.Player); Assert.IsTrue(restored.CanSave);
        Assert.AreEqual(37, restored.World.CurrentSceneId);
        Assert.IsNull(restored.Events); Assert.IsNull(restored.Party); Assert.IsNull(restored.Travel);
    }

    [TestMethod]
    public void Normalized32BitRoleValuesRoundTripWithoutWordTruncation()
    {
        var source = Session(); var party = source.Party!;
        party.AddAttribute(0, RoleWords.Attack, 40000);
        party.SetWord(0, RoleWords.Experience, 100000);
        party.SetWord(0, RoleWords.MagicLevelBegin, 70000);
        party.AddRoleItem(319, 2, 40000);
        party.SetWord(319, RoleWords.Hp, -40000);
        var restored = Restore(source, JsonCopy(source.CaptureSnapshot()));
        Assert.AreEqual(40000, restored.Party!.GetWord(0, RoleWords.Attack));
        Assert.AreEqual(100000, restored.Party.GetWord(0, RoleWords.Experience));
        Assert.AreEqual(70000, restored.Party.GetWord(0, RoleWords.MagicLevelBegin));
        Assert.AreEqual(40000, restored.Party.GetWord(319, RoleWords.TakingCountBegin));
        Assert.AreEqual(-40000, restored.Party.GetWord(319, RoleWords.Hp));
    }

    [TestMethod]
    [DataRow("version")] [DataRow("position")] [DataRow("scene")] [DataRow("inventory")]
    [DataRow("itemIds")] [DataRow("variables")] [DataRow("roles")] [DataRow("roleIdentity")]
    [DataRow("carriedItemId")] [DataRow("members")] [DataRow("ownership")]
    [DataRow("layers")] [DataRow("eventTile")] [DataRow("metadata")] [DataRow("duplicateScene")]
    [DataRow("eventToken")] [DataRow("completedProgram")] [DataRow("camera")] [DataRow("fade")]
    public void InvalidSnapshotCannotAffectTheExistingSession(string corruption)
    {
        var source = Session(); var snapshot = JsonCopy(source.CaptureSnapshot());
        snapshot = corruption switch
        {
            "version" => snapshot with { Version = 99 }, "position" => snapshot with { Position = new(64, 0) },
            "scene" => snapshot with { SceneId = 1 }, "inventory" => snapshot with { Inventory = new() { [0] = -1 } },
            "itemIds" => snapshot with { ItemIds = [0, 0] }, "variables" => snapshot with { Variables = new() { ["other:missing"] = 1 } },
            "roles" => snapshot with { Party = snapshot.Party! with { Roles = [] } },
            "members" => snapshot with { Party = snapshot.Party! with { Members = [0, 1, 1, -1, -1, -1] } },
            "ownership" => snapshot with { Party = snapshot.Party! with { ItemUsers = new() { [2] = 999 } } },
            "layers" => snapshot with { Scenes = [snapshot.Scenes[0] with { Layers = [] }] },
            "metadata" => snapshot with { Scenes = [snapshot.Scenes[0] with { Metadata = [] }] },
            "duplicateScene" => snapshot with { Scenes = [snapshot.Scenes[0], snapshot.Scenes[0]] },
            "eventToken" => snapshot with { Events = snapshot.Events! with { Token = long.MaxValue } },
            "completedProgram" => snapshot with { Events = snapshot.Events! with { CompletedPrograms = [999] } },
            "camera" => snapshot with { Camera = new(double.NaN, 1) }, "fade" => snapshot with { FadeOpacity = 2 },
            _ => snapshot
        };
        if (corruption == "roleIdentity") snapshot.Party!.Roles[1][RoleWords.Id] = 2;
        if (corruption == "carriedItemId") snapshot.Party!.Roles[0][RoleWords.TakingItemBegin] = 65536;
        if (corruption == "eventTile") snapshot.Scenes[0].Layers[3 * 4096] = 200;
        Assert.Throws<ArgumentException>(() => Restore(source, snapshot));
        Assert.AreEqual(3, source.Inventory[0]); Assert.AreEqual(100, source.Party!.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(0, source.World.CurrentSceneId); Assert.AreEqual(1, source.Events!.CompletedCount);
        Assert.IsTrue(source.CanSave);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void OriginalLibraryCheckpointRestoresAll320RolesWithoutRunningTheOpening()
    {
        var library = OriginalLibraryFixture.Library; var view = OriginalLibraryFixture.OpenFreeScene(70);
        var source = new GameSession(view.Scene, view.Story, worldMap: library.MainMap,
            travelStart: library.GameData.TravelStart, battles: library.Battles, startInitialProgram: false);
        var snapshot = JsonCopy(source.CaptureSnapshot()); var restored = Restore(source, snapshot);
        Assert.AreEqual(320, restored.Party!.Roles.Count); Assert.AreEqual(0, restored.Events!.CompletedCount);
        foreach (var id in source.Party!.Roles.Keys)
            for (var word = 0; word < RoleWords.Count; word++)
                Assert.AreEqual(source.Party.GetWord(id, word), restored.Party.GetWord(id, word), $"Role {id}, word {word}");
        Assert.AreEqual(source.Player, restored.Player); Assert.AreEqual(source.Travel!.Snapshot, restored.Travel!.Snapshot);
    }
}
