using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class PartyEventIntegrationTests
{
    private static EventCommand C(int id, EventOperation op, params int[] args) => new(id, op, args);
    private static RoleDefinition Role(int id, params (int Word, int Value)[] changes)
    {
        var words = new int[RoleWords.Count]; words[0] = id;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, $"Role {id}", "", words);
    }
    private static StoryCatalog Story(EventProgram[] programs, PartyDefinition? party = null, WorldDefinition? world = null,
        IReadOnlyCollection<int>? itemIds = null) => new(1, null, 0, null, Direction.Down, programs, [],
        Enumerable.Range(0, 200).ToDictionary(id => id, id => $"Item {id}"), new Dictionary<int, int>(),
        unavailablePrograms: new Dictionary<int, string> { [932] = "Invitation target is not implemented." },
        variableNames: new Dictionary<int, string> { [0] = "test:party/result" }, party: party, world: world,
        initialItemIds: itemIds);
    private static void Start(GameSession session, int program = 1)
    {
        Assert.IsTrue(session.Events!.TryStart(program, 0, session.Player.Position), session.Events.Error);
        Assert.IsNull(session.Events.Error);
    }
    private static void Confirm(GameSession session)
    {
        var token = session.Events!.Pending!.Token;
        Assert.IsTrue(session.Events.Confirm(token));
        Assert.IsFalse(session.Events.Confirm(token));
        Assert.IsNull(session.Events.Error);
    }

    [TestMethod]
    public void RecruitmentTransfersItemsOnceAndPreservesZeroQuantityLegacyIds()
    {
        var party = new PartyDefinition([Role(0), Role(1,
            (RoleWords.TakingItemBegin, 0), (RoleWords.TakingCountBegin, 3),
            (RoleWords.TakingItemBegin + 1, 1), (RoleWords.TakingCountBegin + 1, 0),
            (RoleWords.TakingItemBegin + 2, 2), (RoleWords.TakingCountBegin + 2, -1))], [0, -1, -1, -1, -1, -1]);
        var recruit = new EventProgram(1, [C(0, EventOperation.JoinParty, 1),
            C(1, EventOperation.BranchItemExists, 1, 2, 5), C(2, EventOperation.BranchItemExists, 2, 3, 5),
            C(3, EventOperation.SetVariable, 0, 1), C(4, EventOperation.End), C(5, EventOperation.End)]);
        var repeat = new EventProgram(2, [C(0, EventOperation.JoinParty, 1), C(1, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([recruit, repeat], party));
        Start(session);
        Assert.IsTrue(session.Party!.Contains(1));
        Assert.AreEqual(3, session.Inventory[0]);
        Assert.IsFalse(session.Inventory.ContainsKey(1)); Assert.IsFalse(session.Inventory.ContainsKey(2));
        Assert.AreEqual(EventPresentationKind.Notice, session.Events!.Pending!.Kind);
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Up).Block);
        Assert.AreEqual(0, session.Variables.Count);
        Confirm(session);
        Assert.AreEqual(1, session.Variables["test:party/result"]);
        Assert.IsFalse(session.IsEventRunning);
        Start(session, 2);
        Assert.IsFalse(session.IsEventRunning); Assert.IsNull(session.Events.Pending);
        Assert.AreEqual(3, session.Inventory[0]);
        Assert.AreEqual(-1, session.Party.GetWord(1, RoleWords.TakingItemBegin));
        Assert.AreEqual(0, session.Events.Trace.Count(trace => trace.Operation == EventOperation.RoleAddItem));
        Assert.AreEqual(2, session.Events.CompletedCount);
        Assert.AreEqual(0, party.Roles[1].Words[RoleWords.TakingItemBegin]);
    }

    [TestMethod]
    public void LeavingCompactsPartyClearsPersonalItemsAndReleasesTheirOwnership()
    {
        var party = new PartyDefinition([Role(0), Role(1,
            (RoleWords.EquipmentBegin, 7), (RoleWords.EquipmentBegin + 1, 8),
            (RoleWords.PracticeItem, 9), (RoleWords.ItemExperience, 123)), Role(2)],
            [0, 1, 2, -1, -1, -1], new Dictionary<int, int> { [7] = 1, [8] = 1, [9] = 1 });
        var leave = new EventProgram(1, [C(0, EventOperation.LeaveParty, 1), C(1, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([leave], party));
        Start(session);
        CollectionAssert.AreEqual(new[] { 0, 2, -1, -1, -1, -1 }, session.Party!.Members.ToArray());
        foreach (var word in new[] { RoleWords.EquipmentBegin, RoleWords.EquipmentBegin + 1, RoleWords.PracticeItem })
            Assert.AreEqual(-1, session.Party.GetWord(1, word));
        Assert.AreEqual(0, session.Party.GetWord(1, RoleWords.ItemExperience));
        Assert.IsTrue(session.Party.ItemUsers.Values.All(user => user == -1));
        Assert.AreEqual(0, session.Inventory.Count);
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, session.Events!.CompletedCount);
    }

    [TestMethod]
    [DataRow(EventOperation.BranchParty, true)] [DataRow(EventOperation.BranchParty, false)]
    [DataRow(EventOperation.BranchPartyFull, true)] [DataRow(EventOperation.BranchPartyFull, false)]
    [DataRow(EventOperation.BranchPartyFemale, true)] [DataRow(EventOperation.BranchPartyFemale, false)]
    [DataRow(EventOperation.BranchRole, true)] [DataRow(EventOperation.BranchRole, false)]
    public void PartyConditionsUseCurrentAuthoritativeState(EventOperation condition, bool expected)
    {
        var roles = Enumerable.Range(0, 6).Select(id => Role(id, (RoleWords.Sex, id == 1 ? 1 : 0),
            (RoleWords.Attack, id == 1 && expected ? 50 : 0))).ToArray();
        var party = new PartyDefinition(roles, expected ? [0, 1, 2, 3, 4, 5] : [0, -1, -1, -1, -1, -1]);
        int[] args = condition switch
        {
            EventOperation.BranchParty => [1, 1, 3],
            EventOperation.BranchRole => [1, RoleWords.Attack, 50, 100, 1, 3],
            _ => [1, 3]
        };
        var program = new EventProgram(1, [C(0, condition, args), C(1, EventOperation.SetVariable, 0, 1),
            C(2, EventOperation.End), C(3, EventOperation.SetVariable, 0, 0), C(4, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([program], party));
        Start(session);
        Assert.AreEqual(expected ? 1 : 0, session.Variables["test:party/result"]);
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, session.Events!.CompletedCount);
    }

    [TestMethod]
    public void RestAndMpCommandsWaitForNoticeConfirmationAndKeepSeverelyHurtOrPoisonedMembers()
    {
        RoleDefinition Wounded(int id, int hurt, int poison) => Role(id, (RoleWords.Hurt, hurt), (RoleWords.Poison, poison),
            (RoleWords.Hp, 3), (RoleWords.MaximumHp, 100), (RoleWords.Mp, 10), (RoleWords.MaximumMp, 80));
        var party = new PartyDefinition([Wounded(0, 32, 0), Wounded(1, 33, 0), Wounded(2, 0, 1)], [0, 1, 2, -1, -1, -1]);
        var program = new EventProgram(1, [C(0, EventOperation.ClearPartyMp), C(1, EventOperation.NoticeRole, 0, RoleWords.Mp),
            C(2, EventOperation.RestParty), C(3, EventOperation.NoticeRole, 0, RoleWords.Mp), C(4, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([program], party));
        Start(session);
        Assert.AreEqual(0, session.Party!.GetWord(0, RoleWords.Mp)); Assert.AreEqual(3, session.Party.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(EventPresentationKind.Notice, session.Events!.Pending!.Kind);
        session.Events.Advance(TimeSpan.FromSeconds(100));
        Assert.AreEqual(3, session.Party.GetWord(0, RoleWords.Hp));
        Confirm(session);
        Assert.AreEqual(100, session.Party.GetWord(0, RoleWords.Hp)); Assert.AreEqual(80, session.Party.GetWord(0, RoleWords.Mp));
        Assert.AreEqual(3, session.Party.GetWord(1, RoleWords.Hp)); Assert.AreEqual(0, session.Party.GetWord(1, RoleWords.Mp));
        Assert.AreEqual(3, session.Party.GetWord(2, RoleWords.Hp)); Assert.AreEqual(0, session.Party.GetWord(2, RoleWords.Mp));
        Confirm(session);
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, session.Events.CompletedCount);
    }

    [TestMethod]
    public void AttributeAndMagicNoticesPauseExactlyOnceAndUseCurrentHpGain()
    {
        var party = new PartyDefinition([Role(0, (RoleWords.MaximumHp, 100), (RoleWords.Hp, 20))], [0, -1, -1, -1, -1, -1]);
        var program = new EventProgram(1, [C(0, EventOperation.AddRoleAttribute, 0, RoleWords.MaximumHp, 10),
            C(1, EventOperation.LearnMagic, 0, 5, 0), C(2, EventOperation.SetMagic, 0, 3, 6, 999), C(3, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([program], party));
        Start(session);
        Assert.AreEqual(110, session.Party!.GetWord(0, RoleWords.Hp));
        StringAssert.Contains(session.Events!.Pending!.Text, "90");
        Assert.AreEqual(0, session.Party.GetWord(0, RoleWords.MagicIdBegin));
        Confirm(session);
        Assert.AreEqual(5, session.Party.GetWord(0, RoleWords.MagicIdBegin));
        Assert.AreEqual(0, session.Party.GetWord(0, RoleWords.MagicIdBegin + 3));
        Confirm(session);
        Assert.AreEqual(6, session.Party.GetWord(0, RoleWords.MagicIdBegin + 3));
        Assert.AreEqual(999, session.Party.GetWord(0, RoleWords.MagicLevelBegin + 3));
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, session.Events.CompletedCount);
    }

    [TestMethod]
    public void MissingPartyInACalledProgramFailsPreflightBeforeEarlierRewards()
    {
        var root = new EventProgram(1, [C(0, EventOperation.AdjustItem, 0, 5), C(1, EventOperation.Call, 2), C(2, EventOperation.End)]);
        var child = new EventProgram(2, [C(0, EventOperation.RestParty), C(1, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([root, child]));
        Assert.IsFalse(session.Events!.TryStart(1, 0, session.Player.Position));
        StringAssert.Contains(session.Events.Error!, "Program 2, command 0");
        Assert.AreEqual(0, session.Inventory.Count); Assert.AreEqual(0, session.Events.CompletedCount);
        Assert.AreEqual(0, session.Events.Trace.Count); Assert.IsNull(session.Events.Pending);
        Assert.IsFalse(session.IsEventRunning); Assert.IsTrue(session.TryMove(Direction.Up).Moved);
    }

    [TestMethod]
    [DataRow(RoleWords.MaximumHp, false)] [DataRow(RoleWords.MaximumMp, true)]
    public void NonpartyHpGainIsSilentButMpGainStillShowsTheOriginalNotice(int maximum, bool showsNotice)
    {
        var current = maximum == RoleWords.MaximumHp ? RoleWords.Hp : RoleWords.Mp;
        var party = new PartyDefinition([Role(0), Role(1, (maximum, 100), (current, 20))], [0, -1, -1, -1, -1, -1]);
        var program = new EventProgram(1, [C(0, EventOperation.AddRoleAttribute, 1, maximum, 10),
            C(1, EventOperation.SetVariable, 0, 1), C(2, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([program], party));
        Start(session);
        Assert.AreEqual(110, session.Party!.GetWord(1, current));
        Assert.AreEqual(showsNotice, session.Events!.Pending is not null);
        if (showsNotice)
        {
            StringAssert.Contains(session.Events.Pending!.Text, "90");
            Assert.AreEqual(0, session.Variables.Count);
            Confirm(session);
        }
        Assert.AreEqual(1, session.Variables["test:party/result"]);
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, session.Events.CompletedCount);
    }

    [TestMethod]
    public void RoleAddItemAndQuietMagicCommandsFlowThroughTheSameRuntimeBeforeRecruitment()
    {
        var party = new PartyDefinition([Role(0), Role(1)], [0, -1, -1, -1, -1, -1]);
        var program = new EventProgram(1, [C(0, EventOperation.RoleAddItem, 1, 7, 3),
            C(1, EventOperation.RoleAddItem, 1, 7, -1), C(2, EventOperation.LearnMagic, 1, 5, 1),
            C(3, EventOperation.JoinParty, 1), C(4, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([program], party));
        Start(session);
        Assert.AreEqual(2, session.Inventory[7]); Assert.AreEqual(5, session.Party!.GetWord(1, RoleWords.MagicIdBegin));
        Assert.AreEqual(EventPresentationKind.Notice, session.Events!.Pending!.Kind);
        StringAssert.Contains(session.Events.Pending.Text, "Item 7");
        Confirm(session);
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, session.Events.CompletedCount);
    }

    private static WorldDefinition HomeWorld()
    {
        var events = Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray();
        foreach (var point in events) point[9] = point[10] = -1;
        events[11][9] = 4; events[11][10] = 5;
        var layers = MovementTests.EmptyLayers();
        layers[(int)MapLayer.Event * SceneDefinition.TileCount + 5 * 64 + 4] = 11;
        return new([new WorldSceneDefinition(70, "Home", new short[26], layers, events)]);
    }

    [TestMethod]
    [DataRow(-1, false, true)] [DataRow(144, false, false)] [DataRow(-1, true, false)]
    public void Fame200AndFourteenBookSlotIdsOpenHomeInvitationUnlessAlreadyOwned(int missingBook, bool invitationOwned, bool opens)
    {
        var party = new PartyDefinition([Role(0, (RoleWords.Fame, 199))], [0, -1, -1, -1, -1, -1]);
        var world = HomeWorld();
        var ids = Enumerable.Range(144, 14).Where(id => id != missingBook).ToList();
        if (invitationOwned) ids.Add(189);
        var program = new EventProgram(1, [C(0, EventOperation.AddFame, 1), C(1, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([program], party, world, ids));
        Start(session);
        Assert.AreEqual(200, session.Party!.GetWord(0, RoleWords.Fame));
        Assert.AreEqual(opens ? 932 : 0, session.World.EventField(70, 11, 2));
        Assert.AreEqual(opens ? 7968 : 0, session.World.EventField(70, 11, 7));
        Assert.AreEqual(0, session.Inventory.Count); // Legacy ID-only book test is independent of positive quantities.
        Assert.AreEqual((short)0, world.Scene(70).Events[11][2]);
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, session.Events!.CompletedCount);
        Start(session); // Repeating fame changes keeps the same invitation placement.
        Assert.AreEqual(201, session.Party.GetWord(0, RoleWords.Fame));
        Assert.AreEqual(opens ? 932 : 0, session.World.EventField(70, 11, 2));
        Assert.AreEqual(4, session.World.EventField(70, 11, 9)); Assert.AreEqual(5, session.World.EventField(70, 11, 10));
        Assert.AreEqual(2, session.Events.CompletedCount);
    }

    [TestMethod]
    public void MissingHomeWorldForFameFailsPreflightBeforeAnyEarlierReward()
    {
        var program = new EventProgram(1, [C(0, EventOperation.AdjustItem, 0, 1), C(1, EventOperation.AddFame, 10), C(2, EventOperation.End)]);
        var party = new PartyDefinition([Role(0, (RoleWords.Fame, 199))], [0, -1, -1, -1, -1, -1]);
        var session = new GameSession(MovementTests.Scene(), Story([program], party));
        Assert.IsFalse(session.Events!.TryStart(1, 0, session.Player.Position));
        Assert.AreEqual(199, session.Party!.GetWord(0, RoleWords.Fame)); Assert.AreEqual(0, session.Inventory.Count);
        Assert.AreEqual(0, session.Events.CompletedCount); Assert.IsFalse(session.IsEventRunning);
    }
}
