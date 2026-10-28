using JinYong.Core.Battles;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class BattleSessionTests
{
    private static RoleDefinition Role(int id, params (int Word, int Value)[] changes)
    {
        var words = new int[RoleWords.Count]; words[0] = id;
        words[RoleWords.Hp] = words[RoleWords.MaximumHp] = 100;
        words[RoleWords.Mp] = words[RoleWords.MaximumMp] = 100;
        words[RoleWords.PhysicalPower] = 100; words[RoleWords.Speed] = 60; words[RoleWords.Level] = 1;
        words[RoleWords.Attack] = 50; words[RoleWords.Defence] = 10; words[RoleWords.MagicIdBegin] = 1;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, $"Role {id}", "", words);
    }
    private static PartyState Party(RoleDefinition[] roles, params int[] members) => new(new PartyDefinition(roles,
        Enumerable.Range(0, 6).Select(slot => slot < members.Length ? members[slot] : -1).ToArray()));
    private static BattleDefinition Definition(int[]? preset = null, int[]? fixedParty = null, int[]? enemies = null)
    {
        var words = new int[BattleWords.Count];
        for (var i = 9; i <= 20; i++) words[i] = -1;
        for (var i = 33; i < 53; i++) words[i] = -1;
        for (var i = 0; i < 6; i++)
        { words[21+i] = 10+i; words[27+i] = 10; }
        for (var i = 0; i < 20; i++)
        { words[53+i] = 13+i; words[73+i] = 10; }
        foreach (var (id, slot) in (preset ?? [0]).Select((id, slot) => (id, slot))) words[9+slot] = id;
        foreach (var (id, slot) in (fixedParty ?? []).Select((id, slot) => (id, slot))) words[15+slot] = id;
        foreach (var (id, slot) in (enemies ?? [1]).Select((id, slot) => (id, slot))) words[33+slot] = id;
        return new(0, words);
    }
    private static BattlefieldDefinition Field(params (BattlefieldLayer Layer, GridPosition Position, short Value)[] changes)
    {
        var layers = new short[8192];
        foreach (var (layer, position, value) in changes) layers[(int)layer * 4096 + position.Y * 64 + position.X] = value;
        return new(0, layers);
    }
    private static Dictionary<int, MagicDefinition> Magics()
    {
        var words = new int[MagicWords.Count]; words[0] = 1; words[MagicWords.NeedMp] = 4;
        for (var i = 0; i < 10; i++) { words[MagicWords.AttackBegin+i] = 100; words[MagicWords.SelectDistanceBegin+i] = 4; }
        return new() { [1] = new(1, "Fixture magic", words) };
    }
    private static BattleSession Ready(PartyState? party = null, BattlefieldDefinition? field = null)
    {
        var session = new BattleSession(Definition(), field ?? Field(), party ?? Party([Role(0), Role(1, (RoleWords.Speed, 30))], 0), Magics());
        Assert.IsTrue(session.ConfirmParty([])); return session;
    }

    [TestMethod]
    public void MandatoryAndChosenFightersUseNativePartyOrderAndFixedPartyBypassesSelection()
    {
        var party = Party(Enumerable.Range(0, 5).Select(id => Role(id)).ToArray(), 0, 1, 2);
        var selection = new BattleSession(Definition(preset: [1], enemies: [3]), Field(), party, Magics());
        Assert.AreEqual(BattlePhase.PartySelection, selection.Phase);
        CollectionAssert.AreEqual(new[] { 1 }, selection.MandatoryRoleIds.ToArray());
        Assert.IsTrue(selection.ConfirmParty([2, 0]));
        CollectionAssert.AreEqual(new[] { 1, 0, 2 }, selection.Combatants.Where(actor => actor.IsFriendly).Select(actor => actor.RoleId).ToArray());
        Assert.AreEqual(new GridPosition(11, 10), selection.Combatants.Single(actor => actor.IsFriendly && actor.RoleId == 0).Position);
        var fixedSession = new BattleSession(Definition(preset: [0], fixedParty: [-1, -1, 3], enemies: [4]), Field(), party, Magics());
        Assert.AreEqual(BattlePhase.Acting, fixedSession.Phase);
        Assert.AreEqual(3, fixedSession.Combatants.Single(actor => actor.IsFriendly).RoleId);
        Assert.AreEqual(new GridPosition(12, 10), fixedSession.Combatants.Single(actor => actor.IsFriendly).Position);
        Assert.IsFalse(fixedSession.ConfirmParty([0]));
    }

    [TestMethod]
    public void EmptySelectionDoesNotStartButAll26NativeSlotsAreSupported()
    {
        var roles = Enumerable.Range(0, 26).Select(id => Role(id)).ToArray(); var party = Party(roles, 0, 1, 2, 3, 4, 5);
        var empty = new BattleSession(Definition(preset: []), Field(), party, Magics());
        Assert.AreEqual(BattleOutcome.Ongoing, empty.EvaluateOutcome());
        Assert.AreEqual(BattlePhase.PartySelection, empty.Phase);
        Assert.IsFalse(empty.ConfirmParty([])); Assert.AreEqual(BattlePhase.PartySelection, empty.Phase);
        var full = new BattleSession(Definition(fixedParty: [0,1,2,3,4,5], enemies: Enumerable.Range(6,20).ToArray()), Field(), party, Magics());
        Assert.AreEqual(26, full.Combatants.Count); Assert.AreEqual(6, full.Combatants.Count(actor => actor.IsFriendly));
        Assert.AreEqual(20, full.Combatants.Count(actor => !actor.IsFriendly));
    }

    [TestMethod]
    public void SameRoleOnOppositeSidesHasAnIndependentEnemyBaselineAndNoPartyHealing()
    {
        var party = Party([Role(0), Role(1)], 0, 1); party.SetWord(1, RoleWords.Hp, 7);
        var battle = new BattleSession(Definition(fixedParty: [1]), Field(), party, Magics());
        var friend = battle.Combatants.Single(actor => actor.IsFriendly); var enemy = battle.Combatants.Single(actor => !actor.IsFriendly);
        Assert.AreSame(party.Roles[1], friend.Role); Assert.AreNotSame(friend.Role, enemy.Role);
        Assert.AreEqual(7, friend.Hp); Assert.AreEqual(100, enemy.Hp);
        party.SetWord(1, RoleWords.Mp, 3); Assert.AreEqual(100, enemy.Mp);
    }

    [TestMethod]
    public void NativePairwiseSpeedSortIsNotStableAndEquipmentChangesRoundMovement()
    {
        var party = Party([Role(0,(RoleWords.Speed,10)),Role(1,(RoleWords.Speed,10)),Role(2,(RoleWords.Speed,20))],0,1);
        var battle = new BattleSession(Definition(fixedParty:[0,1], enemies:[2]),Field(),party,Magics());
        CollectionAssert.AreEqual(new[] {2,1,0}, battle.Combatants.Select(actor=>actor.RoleId).ToArray());
        var itemWords = new int[95]; itemWords[ItemWords.AddSpeed] = 20;
        party.SetWord(0,RoleWords.EquipmentBegin,0);
        var equipped = new BattleSession(Definition(fixedParty:[0],enemies:[2]),Field(),party,Magics(),
            new Dictionary<int,ItemDefinition>{[0]=new(0,"Speed equipment",itemWords)});
        Assert.AreEqual(0,equipped.CurrentActor!.RoleId); Assert.AreEqual(30,equipped.EffectiveSpeed(equipped.CurrentActor));
        Assert.AreEqual(2,equipped.CurrentActor.RoundValue);
    }

    [TestMethod]
    public void MovementUsesOccupancyAndBudgetAndDoesNotEndTheActorsAction()
    {
        var battle = Ready(); var actor=battle.CurrentActor!;
        Assert.AreEqual(4,actor.RoundValue);
        Assert.IsTrue(battle.TryMove(new(12,10))); Assert.AreEqual(2,actor.RoundValue);
        Assert.AreSame(actor,battle.CurrentActor); Assert.IsFalse(actor.ActionDone);
        Assert.IsNull(battle.OccupantAt(new(10,10))); Assert.AreSame(actor,battle.OccupantAt(new(12,10)));
        Assert.IsFalse(battle.TryMove(new(13,10))); Assert.IsFalse(battle.TryMove(new(16,10)));
        Assert.AreEqual(new GridPosition(12,10),actor.Position);
        var distances=battle.ReachableMoves(); Assert.IsFalse(distances.ContainsKey(new(13,10)));
    }

    [TestMethod]
    public void TargetingIgnoresGroundAndFightersButStillRespectsBuildings()
    {
        var field=Field((BattlefieldLayer.Ground,new(11,10),358),(BattlefieldLayer.Building,new(10,9),4));
        var battle=Ready(field:field);
        Assert.IsFalse(BattlePathing.Distances(field,battle.Combatants,new(10,10),maximum:1).ContainsKey(new(11,10)));
        var targets=BattlePathing.Distances(field,battle.Combatants,new(10,10),targeting:true,maximum:4);
        Assert.AreEqual(1,targets[new(11,10)]); Assert.AreEqual(3,targets[new(13,10)]);
        Assert.IsFalse(targets.ContainsKey(new(10,9)));
        Assert.IsNull(BattlePathing.FindPath(field,battle.Combatants,new(10,10),new(13,10)));
    }

    [TestMethod]
    public void WaitDefersActorAndPreservesTheirMovementUntilAfterOtherActors()
    {
        var party=Party([Role(0),Role(1,(RoleWords.Speed,45)),Role(2,(RoleWords.Speed,30))],0,1);
        var battle=new BattleSession(Definition(fixedParty:[0,1],enemies:[2]),Field(),party,Magics());
        var hero=battle.CurrentActor!; Assert.IsTrue(battle.Wait()); Assert.AreEqual(1,battle.CurrentActor!.RoleId);
        Assert.IsTrue(battle.Rest()); Assert.AreEqual(2,battle.CurrentActor!.RoleId);
        Assert.IsTrue(battle.Rest()); Assert.AreSame(hero,battle.CurrentActor); Assert.AreEqual(4,hero.RoundValue);
        Assert.AreEqual(1,battle.Round); Assert.IsTrue(battle.Wait()); Assert.AreSame(hero,battle.CurrentActor);
    }

    [TestMethod]
    public void RestMatchesOriginalSeedOneVectorAndUsesSharedRandomProviderWhenSupplied()
    {
        var party=Party([Role(0,(RoleWords.Speed,0),(RoleWords.PhysicalPower,50),(RoleWords.Hp,95),
            (RoleWords.Mp,48),(RoleWords.MaximumMp,50)),Role(1,(RoleWords.Speed,0))],0);
        var battle=Ready(party); Assert.IsTrue(battle.Rest());
        Assert.AreEqual(55,party.GetWord(0,RoleWords.PhysicalPower)); Assert.AreEqual(99,party.GetWord(0,RoleWords.Hp));
        Assert.AreEqual(50,party.GetWord(0,RoleWords.Mp));
        var calls=new List<int>();
        var external=new BattleSession(Definition(),Field(),Party([Role(0,(RoleWords.Speed,0),(RoleWords.PhysicalPower,50)),Role(1,(RoleWords.Speed,0))],0),
            Magics(),randomSeed:17,random:bound=>{calls.Add(bound);return bound-1;});
        external.ConfirmParty([]); external.Rest();
        CollectionAssert.AreEqual(new[]{3,3,3},calls.ToArray()); Assert.AreEqual(17u,external.RandomState);
        calls.Clear();
        var boundOne = new BattleSession(Definition(),Field(),Party([Role(0,(RoleWords.Speed,0),(RoleWords.PhysicalPower,27)),
            Role(1,(RoleWords.Speed,0))],0),Magics(),random:bound=>{calls.Add(bound);return 0;});
        boundOne.ConfirmParty([]); boundOne.Rest();
        CollectionAssert.AreEqual(new[]{3},calls.ToArray()); // HP/MP bounded(1) calls consume no shared random value.
    }

    [TestMethod]
    public void RoundStatusUsesOriginalFixedLossAndStrictZeroVersusNegativeFloor()
    {
        var party=Party([Role(0,(RoleWords.Hurt,20),(RoleWords.PhysicalPower,0)),Role(1,(RoleWords.Speed,30))],0);
        var battle=Ready(party); battle.Rest(); battle.Rest();
        Assert.AreEqual(99,party.GetWord(0,RoleWords.Hp)); Assert.AreEqual(2,battle.Round);
        var zeroParty=Party([Role(0,(RoleWords.Hp,2),(RoleWords.Hurt,40),(RoleWords.PhysicalPower,0)),Role(1,(RoleWords.Speed,30))],0);
        var zero=Ready(zeroParty); zero.Rest(); zero.Rest();
        Assert.AreEqual(0,zeroParty.GetWord(0,RoleWords.Hp)); Assert.AreEqual(BattleOutcome.Defeat,zero.Outcome);
        var negativeParty=Party([Role(0,(RoleWords.Hp,1),(RoleWords.Hurt,60),(RoleWords.PhysicalPower,0)),Role(1,(RoleWords.Speed,30))],0);
        var negative=Ready(negativeParty); negative.Rest(); negative.Rest();
        Assert.AreEqual(1,negativeParty.GetWord(0,RoleWords.Hp)); Assert.AreEqual(BattleOutcome.Ongoing,negative.Outcome);
    }

    [TestMethod]
    public void RealMagicDamageEndsInVictoryOnlyAfterAnOpponentActuallyDies()
    {
        // Deliberately strong, independently authored actor: not an original balance claim.
        var party=Party([Role(0,(RoleWords.Attack,500)),Role(1,(RoleWords.Speed,30))],0);
        var battle=Ready(party); var before=party.GetWord(0,RoleWords.PhysicalPower);
        Assert.AreEqual(BattleOutcome.Ongoing,battle.Outcome);
        Assert.IsTrue(battle.AttackMagic(0,new(13,10)),battle.LastError);
        Assert.AreEqual(BattleOutcome.Victory,battle.Outcome); Assert.AreEqual(BattlePhase.Finished,battle.Phase);
        Assert.IsTrue(battle.LastDamage.Count>0); Assert.IsNull(battle.OccupantAt(new(13,10)));
        Assert.AreEqual(before-3,party.GetWord(0,RoleWords.PhysicalPower));
        Assert.IsFalse(battle.Rest()); Assert.IsFalse(battle.TryMove(new(11,10)));
    }

    [TestMethod]
    public void DoubleAttackUsesTwoIterationsButChargesPhysicalPowerOnlyOnce()
    {
        var party=Party([Role(0,(RoleWords.AttackTwice,1),(RoleWords.MagicLevelBegin,200)),
            Role(1,(RoleWords.Hp,1000),(RoleWords.MaximumHp,1000),(RoleWords.Speed,30))],0);
        var battle=new BattleSession(Definition(),Field(),party,Magics(),random:_=>0);battle.ConfirmParty([]);
        Assert.IsTrue(battle.AttackMagic(0,new(13,10)),battle.LastError);
        Assert.AreEqual(2,battle.LastDamage.Count); Assert.AreEqual(202,party.GetWord(0,RoleWords.MagicLevelBegin));
        Assert.AreEqual(92,party.GetWord(0,RoleWords.Mp)); Assert.AreEqual(97,party.GetWord(0,RoleWords.PhysicalPower));
        Assert.AreEqual(3,battle.HpCostScale); Assert.AreEqual(1,battle.CurrentActor!.RoleId);
    }

    [TestMethod]
    public void FinalStatusRunsOnlyOnceAndCannotChangeTheFrozenOutcome()
    {
        var party=Party([Role(0,(RoleWords.Attack,500),(RoleWords.Hurt,60),(RoleWords.Hp,3)),Role(1,(RoleWords.Speed,30))],0);
        var battle=Ready(party);
        Assert.IsFalse(battle.CompleteAfterSettlement());
        Assert.IsTrue(battle.AttackMagic(0,new(13,10)),battle.LastError);
        Assert.AreEqual(BattleOutcome.Victory,battle.Outcome);
        Assert.IsTrue(battle.CompleteAfterSettlement()); Assert.AreEqual(0,party.GetWord(0,RoleWords.Hp));
        Assert.AreEqual(BattleOutcome.Victory,battle.EvaluateOutcome());
        Assert.IsFalse(battle.CompleteAfterSettlement()); Assert.AreEqual(0,party.GetWord(0,RoleWords.Hp));
    }

    [TestMethod]
    public void EnemyWithoutMagicRestsInsteadOfInventingABasicAttack()
    {
        var party=Party([Role(0,(RoleWords.Speed,15)),Role(1,(RoleWords.Speed,60),(RoleWords.MagicIdBegin,0))],0);
        var battle=Ready(party); Assert.IsFalse(battle.CurrentActor!.IsFriendly);
        Assert.IsTrue(battle.StepEnemy(),battle.LastError); Assert.AreEqual(100,party.GetWord(0,RoleWords.Hp));
        Assert.AreEqual(0,battle.CurrentActor!.RoleId); Assert.AreEqual(0,battle.LastDamage.Count);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualTrainingBattleUsesOriginalPartyPlacementAndIsolatedHuFeiBaseline()
    {
        var library=OriginalLibraryFixture.Library; var catalog=library.Battles!;
        var party=new PartyState(library.GameData.Party!); var before=party.GetWord(0,RoleWords.Hp);
        var definition=catalog.Definitions[0];
        var battle=new BattleSession(definition,catalog.Fields[definition.BattlefieldId],party,catalog.Magics,catalog.Items);
        CollectionAssert.AreEqual(new[]{0},battle.MandatoryRoleIds.ToArray()); Assert.IsTrue(battle.ConfirmParty([]));
        var hero=battle.Combatants.Single(actor=>actor.IsFriendly);var enemy=battle.Combatants.Single(actor=>!actor.IsFriendly);
        Assert.AreEqual(0,hero.RoleId); Assert.AreEqual(new GridPosition(32,20),hero.Position);
        Assert.AreEqual(1,enemy.RoleId); Assert.AreEqual(new GridPosition(21,23),enemy.Position);
        Assert.AreEqual(before,hero.Hp); Assert.AreNotSame(party.Roles[1],enemy.Role);
        Assert.IsTrue(battle.Combatants.All(actor=>actor.IsAlive)); Assert.AreEqual(BattleOutcome.Ongoing,battle.Outcome);
    }
}
