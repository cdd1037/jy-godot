using JinYong.Core.Battles;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

/// <summary>Synthetic strong/weak fighters exercise the real battle and event pipeline, without forcing an outcome.</summary>
[TestClass]
public sealed class GameSessionBattleTests
{
    private static EventCommand C(int id, EventOperation op, params int[] args) => new(id, op, args);
    private static RoleDefinition Role(int id, int attack, int hp, int speed)
    {
        var words = new int[RoleWords.Count]; words[RoleWords.Id] = id;
        words[RoleWords.Hp] = words[RoleWords.MaximumHp] = hp;
        words[RoleWords.Mp] = words[RoleWords.MaximumMp] = 100;
        words[RoleWords.PhysicalPower] = 100; words[RoleWords.Level] = 1;
        words[RoleWords.Attack] = attack; words[RoleWords.Speed] = speed;
        words[RoleWords.Defence] = 10; words[RoleWords.MagicIdBegin] = 1;
        words[RoleWords.MagicLevelBegin] = 200;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        return new(id, $"Fixture role {id}", "", words);
    }
    private static BattleDefinition Definition()
    {
        var words = new int[BattleWords.Count];
        for (var i = BattleWords.PresetPartyBegin; i < BattleWords.PartyXBegin; i++) words[i] = -1;
        for (var i = BattleWords.EnemyBegin; i < BattleWords.EnemyXBegin; i++) words[i] = -1;
        words[BattleWords.PresetPartyBegin] = 0;
        words[BattleWords.PartyXBegin] = 10; words[BattleWords.PartyYBegin] = 10;
        words[BattleWords.EnemyBegin] = 1;
        words[BattleWords.EnemyXBegin] = 11; words[BattleWords.EnemyYBegin] = 10;
        return new(0, words);
    }
    private static MagicDefinition Magic()
    {
        var words = new int[MagicWords.Count]; words[MagicWords.Id] = 1; words[MagicWords.NeedMp] = 4;
        for (var i = 0; i < MagicWords.LevelCount; i++)
        { words[MagicWords.AttackBegin + i] = 100; words[MagicWords.SelectDistanceBegin + i] = 4; }
        return new(1, "Fixture magic", words);
    }
    private static EventProgram BranchProgram(bool rewardBeforeBattle = false, int battleId = 0)
    {
        var commands = new List<EventCommand>();
        if (rewardBeforeBattle) commands.Add(C(0, EventOperation.AdjustItem, 0, 5));
        commands.AddRange([C(1, EventOperation.Battle, battleId, 10, 20, 0),
            C(10, EventOperation.AdjustItem, 0, 1), C(11, EventOperation.SetVariable, 0, 1), C(12, EventOperation.End),
            C(20, EventOperation.AdjustItem, 0, 2), C(21, EventOperation.SetVariable, 0, -1), C(22, EventOperation.End)]);
        return new(1, commands);
    }
    private static GameSession Session(EventProgram? program = null, bool defeat = false, bool resources = true,
        bool progression = true, bool medicine = false)
    {
        var party = new PartyDefinition(defeat ? [Role(0, 0, 10, 15), Role(1, 500, 100, 60)] :
            [Role(0, 500, 100, 60), Role(1, 0, medicine ? 100 : 10, 30)], [0, -1, -1, -1, -1, -1]);
        var metadata = new short[26]; metadata[8] = -1;
        var points = Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray();
        foreach (var point in points) point[9] = point[10] = -1;
        var worldScene = new WorldSceneDefinition(0, "Fixture scene", metadata, MovementTests.EmptyLayers(), points);
        var story = new StoryCatalog(0, null, 0, null, Direction.Down, [program ?? BranchProgram()],
            Enumerable.Range(100, 3).Select(id => new DialogueDefinition(id, [$"Random {id}"])),
            new Dictionary<int, string> { [0] = "Fixture item" }, new Dictionary<int, int> { [0] = 2 },
            variableNames: new Dictionary<int, string> { [0] = "test:battle/result" },
            party: party, world: new WorldDefinition([worldScene]));
        // Large synthetic thresholds keep these action/RNG tests below a level-up.
        var rules = progression ? new ProgressionDefinition(Enumerable.Range(0, 30).Select(i => i * 1000).ToArray(),
            999, 999, 110, 1, 1, 2, 4, 5) : null;
        var itemWords = new int[ItemWords.Count]; itemWords[ItemWords.ItemType] = 3; itemWords[ItemWords.AddHp] = 20;
        var catalog = resources ? new BattleCatalog([Definition()], [new BattlefieldDefinition(0, new short[8192])],
            [Magic()], medicine ? [new ItemDefinition(0, "Fixture medicine", itemWords)] : [], party, rules) : null;
        return new(worldScene.ToSceneDefinition(new(19, 20)), story, battles: catalog);
    }
    private static BattleSession Start(GameSession session)
    {
        Assert.IsTrue(session.Events!.TryStart(1, 0, session.Player.Position), session.Events.Error);
        Assert.IsNotNull(session.Battle); Assert.IsNotNull(session.Events.PendingBattle);
        return session.Battle;
    }
    private static void CompletePresentation(GameSession session)
    {
        var battle = session.Battle ?? throw new InvalidOperationException("No active fixture battle.");
        for (var budget = 0; battle.IsPresenting && budget < 1000; budget++)
        {
            var frame = battle.Presentation!;
            if (frame.WaitingForConfirmation) Assert.IsTrue(battle.ConfirmPresentation(frame.Token));
            else session.Advance(frame.Duration);
        }
        Assert.IsFalse(battle.IsPresenting, "Battle presentation did not reach its next input boundary.");
    }
    private static void CompleteEnemyTurn(GameSession session)
    {
        var battle = session.Battle!;
        for (var budget = 0; battle.Outcome == BattleOutcome.Ongoing && battle.CurrentActor?.IsFriendly == false && budget < 100; budget++)
        {
            if (battle.IsPresenting) CompletePresentation(session);
            else Assert.IsTrue(battle.StepEnemy(), battle.LastError);
        }
        Assert.IsFalse(battle.IsPresenting);
        Assert.IsTrue(battle.Outcome != BattleOutcome.Ongoing || battle.CurrentActor?.IsFriendly == true,
            "Enemy continuation did not finish its action.");
    }
    private static void HeroWins(GameSession session)
    {
        var battle = session.Battle!;
        Assert.IsTrue(battle.ConfirmParty([]), battle.LastError);
        Assert.IsTrue(battle.CurrentActor!.IsFriendly);
        Assert.IsTrue(battle.AttackMagic(0, battle.Combatants.Single(actor => !actor.IsFriendly).Position), battle.LastError);
        Assert.IsTrue(battle.IsPresenting); Assert.AreEqual(BattleOutcome.Ongoing, battle.Outcome);
        Assert.IsFalse(session.FinishBattle());
        CompletePresentation(session);
        Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
    }

    [TestMethod]
    public void BattleMedicineConsumesTheActualSessionBackpackBeforeStoryResumes()
    {
        var session = Session(medicine: true); var party = session.Party!; var events = session.Events!;
        var battle = Start(session); Assert.IsTrue(battle.ConfirmParty([]));
        for (var remaining = 1; remaining >= 0; remaining--)
        {
            Assert.IsTrue(battle.CurrentActor!.IsFriendly);
            party.SetWord(0, RoleWords.Hp, 50);
            Assert.IsTrue(battle.UseBattleItem(0), battle.LastError);
            Assert.AreEqual(remaining, session.Inventory.GetValueOrDefault(0));
            Assert.AreEqual(remaining > 0, ((IEventWorld)session).ItemExists(0));
            Assert.AreSame(party.Roles[0], battle.Combatants.Single(actor => actor.IsFriendly).Role);
            Assert.IsTrue(party.GetWord(0, RoleWords.Hp) > 50);
            var frame = battle.Presentation!;
            Assert.AreEqual(BattlePresentationStage.Item, frame.Stage); Assert.IsTrue(frame.WaitingForConfirmation);
            session.Advance(TimeSpan.FromMinutes(1)); Assert.IsTrue(battle.IsPresenting);
            Assert.IsTrue(battle.CurrentActor!.IsFriendly); Assert.IsFalse(battle.StepEnemy());
            Assert.IsFalse(battle.ConfirmPresentation(frame.Token + 1)); Assert.IsTrue(battle.ConfirmPresentation(frame.Token));
            Assert.IsFalse(battle.ConfirmPresentation(frame.Token));
            CompleteEnemyTurn(session);
        }
        Assert.IsTrue(battle.AttackMagic(0, battle.Combatants.Single(actor => !actor.IsFriendly).Position), battle.LastError);
        Assert.IsTrue(battle.IsPresenting); Assert.IsFalse(session.FinishBattle()); CompletePresentation(session);
        Assert.AreEqual(BattleOutcome.Victory, battle.Outcome); Assert.IsTrue(session.FinishBattle());
        Assert.AreSame(events, session.Events); Assert.AreEqual(1, session.Inventory[0]); // Only the actual post-battle reward.
        Assert.AreNotEqual(1u, events.CaptureIdleState().RandomState);
        var snapshot = session.CaptureSnapshot();
        var restored = GameSession.Restore(session.Definition, session.Story, snapshot, battles: session.BattleResources);
        Assert.AreEqual(1, restored.Inventory[0]); Assert.AreEqual(party.GetWord(0, RoleWords.Hp), restored.Party!.GetWord(0, RoleWords.Hp));
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ActualDamageResumesTheCorrectBranchAndPreservesTheSessionState(bool defeat)
    {
        var session = Session(defeat: defeat); var party = session.Party; var world = session.World;
        var scene = session.Scene; var events = session.Events; var player = session.Player;
        ((IEventWorld)session).SetVariable(0, 9);
        world.SetTile(0, MapLayer.Building, new(12, 12), 100); world.SetEventField(0, 5, 0, 1);
        var battle = Start(session); var token = events!.PendingBattle!.Token;
        Assert.IsFalse(session.CanSave);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.CaptureSnapshot());
        Assert.IsFalse(session.FinishBattle()); Assert.IsFalse(events.Confirm(token));
        Assert.IsFalse(events.ResumeBattle(token)); Assert.IsFalse(events.ResumeBattle(token + 1));
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Right).Block);
        Assert.IsTrue(battle.ConfirmParty([]), battle.LastError);
        if (defeat)
        {
            Assert.IsFalse(battle.CurrentActor!.IsFriendly);
            CompleteEnemyTurn(session);
        }
        else
        {
            Assert.IsTrue(battle.AttackMagic(0, new(11, 10)), battle.LastError);
            Assert.IsTrue(battle.IsPresenting); Assert.IsFalse(session.FinishBattle()); CompletePresentation(session);
        }
        var expected = defeat ? BattleOutcome.Defeat : BattleOutcome.Victory;
        Assert.AreEqual(expected, battle.Outcome);
        // Even a resolved battlefield cannot resume its event before authoritative settlement.
        Assert.IsFalse(events.ResumeBattle(token)); Assert.IsFalse(events.Confirm(token));
        Assert.IsTrue(session.FinishBattle()); Assert.IsNull(session.Battle);
        Assert.AreEqual(expected, session.LastBattleOutcome); Assert.IsNotNull(session.LastBattleSettlement);
        Assert.AreEqual(defeat ? -1 : 1, session.Variables["test:battle/result"]);
        Assert.AreEqual(defeat ? 4 : 3, session.Inventory[0]);
        Assert.AreSame(party, session.Party); Assert.AreSame(world, session.World);
        Assert.AreSame(scene, session.Scene); Assert.AreSame(events, session.Events);
        Assert.AreSame(party!.Roles[0], battle.Combatants.Single(actor => actor.IsFriendly).Role);
        Assert.AreNotSame(party.Roles[1], battle.Combatants.Single(actor => !actor.IsFriendly).Role);
        Assert.AreEqual(player, session.Player);
        Assert.AreEqual((short)100, session.Scene.At(MapLayer.Building, new(12, 12)));
        Assert.AreEqual((short)1, session.Scene.Events[5][0]);
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, events.CompletedCount);
        Assert.IsNull(events.PendingBattle); Assert.IsNull(events.Error);
        var xp = party.GetWord(0, RoleWords.Experience); var hp = party.GetWord(0, RoleWords.Hp);
        Assert.IsFalse(session.FinishBattle()); Assert.IsFalse(events.ResumeBattle(token));
        Assert.AreEqual(xp, party.GetWord(0, RoleWords.Experience)); Assert.AreEqual(hp, party.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(defeat ? 4 : 3, session.Inventory[0]); Assert.AreEqual(1, events.CompletedCount);
    }

    [TestMethod]
    [DataRow(false, true, 0)] [DataRow(true, false, 0)] [DataRow(true, true, 99)]
    public void MissingBattleDependenciesRejectTheEntireEventBeforeEarlierRewards(bool resources, bool progression, int battleId)
    {
        var session = Session(BranchProgram(rewardBeforeBattle: true, battleId: battleId), resources: resources, progression: progression);
        var hp = session.Party!.GetWord(0, RoleWords.Hp);
        Assert.IsFalse(session.Events!.TryStart(1, 0, session.Player.Position));
        Assert.IsNotNull(session.Events.Error); Assert.IsNull(session.Battle); Assert.IsNull(session.Events.PendingBattle);
        Assert.AreEqual(2, session.Inventory[0]); Assert.AreEqual(0, session.Variables.Count);
        Assert.AreEqual(hp, session.Party.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(0, session.Events.Trace.Count); Assert.AreEqual(0, session.Events.CompletedCount);
        Assert.IsFalse(session.FinishBattle()); Assert.IsNull(session.LastBattleSettlement);
    }

    [TestMethod]
    public void SettlementRecoveryPrecedesTheSingleFinalStatusTick()
    {
        var session = Session(); session.Party!.SetWord(0, RoleWords.Hp, 3);
        session.Party.SetWord(0, RoleWords.Hurt, 60);
        var battle = Start(session); HeroWins(session);
        Assert.AreEqual(3, session.Party.GetWord(0, RoleWords.Hp));
        Assert.IsTrue(session.FinishBattle());
        Assert.AreEqual(17, session.Party.GetWord(0, RoleWords.Hp)); // MaxHP/5=20, then hurt/20=3.
        Assert.AreEqual(BattleOutcome.Victory, session.LastBattleOutcome);
        Assert.IsFalse(battle.CompleteAfterSettlement()); Assert.IsFalse(session.FinishBattle());
        Assert.AreEqual(17, session.Party.GetWord(0, RoleWords.Hp));
    }

    private static EventProgram TwoBattles() => new(1,
        [C(0, EventOperation.Battle, 0, 1, 1, 0), C(1, EventOperation.RandomDialogue, 100, 3, 0, 0),
         C(2, EventOperation.Battle, 0, 3, 3, 0), C(3, EventOperation.RandomDialogue, 100, 3, 0, 0), C(4, EventOperation.End)]);
    private static int Roll(ref uint state, int bound)
    {
        state = unchecked(state * 0x41C64E6D + 0x3039);
        return (int)(((state >> 16) & 0x7FFF) % (uint)bound);
    }
    private static void ExpectHitAndDialogue(GameSession session, ref uint state)
    {
        // This fixture has positive HP damage: two bounded20 rolls then one proficiency bounded2 roll.
        Roll(ref state, 20); Roll(ref state, 20); Roll(ref state, 2);
        var dialogue = 100 + Roll(ref state, 3);
        Assert.IsTrue(session.FinishBattle());
        Assert.AreEqual(dialogue, session.Events!.Pending!.DialogueId);
        Assert.IsNull(session.Events.PendingBattle);
    }

    [TestMethod]
    public void ConsecutiveBattlesShareTheEventRandomStreamAndCarryTheOriginalHpCostScale()
    {
        var session = Session(TwoBattles()); var party = session.Party; var events = session.Events;
        uint state = 1; var first = Start(session); HeroWins(session);
        Assert.AreEqual(3, first.HpCostScale); Assert.AreEqual(96, party!.GetWord(0, RoleWords.Mp));
        ExpectHitAndDialogue(session, ref state);
        Assert.IsTrue(events!.Confirm(events.Pending!.Token));
        var second = session.Battle!; Assert.AreNotSame(first, second);
        Assert.AreEqual(3, second.HpCostScale); Assert.AreEqual(BattleOutcome.Ongoing, second.Outcome);
        Assert.AreEqual(10, party.GetWord(1, RoleWords.Hp)); // Enemy baseline never mutated the shared role.
        HeroWins(session); ExpectHitAndDialogue(session, ref state);
        Assert.AreEqual(92, party.GetWord(0, RoleWords.Mp));
        Assert.IsTrue(events.Confirm(events.Pending!.Token));
        Assert.IsFalse(session.IsEventRunning); Assert.AreEqual(1, events.CompletedCount);
        Assert.AreSame(events, session.Events); Assert.AreSame(party, session.Party);
        Assert.IsFalse(session.FinishBattle()); Assert.IsNull(events.Error);
    }

    [TestMethod]
    public void EmptyStrikeInTheNextBattleUsesThePreviousBattlesHpCostScale()
    {
        var session = Session(TwoBattles()); Start(session); HeroWins(session); Assert.IsTrue(session.FinishBattle());
        Assert.IsTrue(session.Events!.Confirm(session.Events.Pending!.Token));
        var second = session.Battle!; Assert.AreEqual(3, second.HpCostScale);
        Assert.IsTrue(second.ConfirmParty([]));
        Assert.IsTrue(second.AttackMagic(0, new(10, 11)), second.LastError);
        Assert.AreEqual(0, second.LastDamage.Count); Assert.AreEqual(3, second.HpCostScale);
        Assert.AreEqual(92, session.Party!.GetWord(0, RoleWords.Mp));
        Assert.IsTrue(second.IsPresenting); CompletePresentation(session); Assert.IsFalse(second.IsPresenting);
        Assert.AreEqual(BattleOutcome.Ongoing, second.Outcome); Assert.IsFalse(session.FinishBattle());
    }

    [TestMethod]
    public void ExplorationCheckpointKeepsTheCrossBattleCostScaleAndRandomStream()
    {
        var source = Session(); Start(source); HeroWins(source); Assert.IsTrue(source.FinishBattle());
        var snapshot = source.CaptureSnapshot(); Assert.AreEqual(3, snapshot.HpCostScale);
        var restored = GameSession.Restore(source.Definition, source.Story, snapshot, battles: source.BattleResources);
        var originalBattle = Start(source); var restoredBattle = Start(restored);
        Assert.AreEqual(3, restoredBattle.HpCostScale);
        Assert.IsTrue(originalBattle.ConfirmParty([])); Assert.IsTrue(restoredBattle.ConfirmParty([]));
        Assert.IsTrue(originalBattle.AttackMagic(0, new(10, 11))); Assert.IsTrue(restoredBattle.AttackMagic(0, new(10, 11)));
        Assert.AreEqual(92, restored.Party!.GetWord(0, RoleWords.Mp));
        Assert.AreEqual(source.Party!.GetWord(0, RoleWords.MagicLevelBegin), restored.Party.GetWord(0, RoleWords.MagicLevelBegin));
        Assert.AreEqual(originalBattle.HpCostScale, restoredBattle.HpCostScale);
        CompletePresentation(source); CompletePresentation(restored);
        Assert.AreEqual(originalBattle.Outcome, restoredBattle.Outcome);
    }
}
