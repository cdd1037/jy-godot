using JinYong.Core.Battles;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class BattleAiTests
{
    private sealed class Oracle(uint seed = 1)
    {
        public uint State = seed;
        public readonly List<int> Bounds = [];
        public int Draw(int bound)
        {
            if (bound <= 1) return 0;
            Bounds.Add(bound); State = unchecked(State * 0x41C64E6D + 0x3039);
            return (int)(((State >> 16) & 0x7FFF) % (uint)bound);
        }
    }
    private static RoleDefinition Role(int id, params (int Word, int Value)[] changes)
    {
        var words = new int[RoleWords.Count]; words[0] = id;
        words[RoleWords.Hp] = words[RoleWords.MaximumHp] = words[RoleWords.Mp] = words[RoleWords.MaximumMp] = 100;
        words[RoleWords.PhysicalPower] = 100; words[RoleWords.Level] = 1; words[RoleWords.Attack] = 50;
        words[RoleWords.Speed] = id == 1 ? 90 : id == 3 ? 50 : 30; words[RoleWords.Morality] = 50;
        words[RoleWords.MagicIdBegin] = 1;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        for (var slot = 0; slot < 4; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, $"AI fixture {id}", "", words);
    }
    private static MagicDefinition Magic(int id = 1, int need = 4)
    {
        var words = new int[MagicWords.Count]; words[0] = id; words[MagicWords.NeedMp] = need;
        for (var level = 0; level < 10; level++) { words[MagicWords.AttackBegin + level] = 100; words[MagicWords.SelectDistanceBegin + level] = 4; }
        return new(id, "AI fixture magic", words);
    }
    private static ItemDefinition Item(int id, params (int Word, int Value)[] changes)
    {
        var words = new int[ItemWords.Count]; words[0] = id; words[ItemWords.ItemType] = 3;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, "AI fixture item", words);
    }
    private static BattleSession Ready(RoleDefinition? enemy = null, RoleDefinition? ally = null, RoleDefinition? hero = null,
        Dictionary<int, ItemDefinition>? items = null, bool far = false, bool presenting = false,
        Dictionary<int, MagicDefinition>? magics = null)
    {
        var roles = new[] { hero ?? Role(0), enemy ?? Role(1), Role(2), ally ?? Role(3) };
        var party = new PartyState(new PartyDefinition(roles, [0, 2, -1, -1, -1, -1]));
        var words = new int[BattleWords.Count];
        for (var slot = 0; slot < 6; slot++) words[BattleWords.PresetPartyBegin + slot] = words[BattleWords.FixedPartyBegin + slot] = -1;
        for (var slot = 0; slot < 20; slot++) words[BattleWords.EnemyBegin + slot] = -1;
        words[BattleWords.FixedPartyBegin] = 0; words[BattleWords.FixedPartyBegin + 1] = 2;
        words[BattleWords.PartyXBegin] = 10; words[BattleWords.PartyYBegin] = 20;
        words[BattleWords.PartyXBegin + 1] = 8; words[BattleWords.PartyYBegin + 1] = 20;
        words[BattleWords.EnemyBegin] = 1; words[BattleWords.EnemyBegin + 1] = 3;
        words[BattleWords.EnemyXBegin] = far ? 20 : 12; words[BattleWords.EnemyYBegin] = 20;
        words[BattleWords.EnemyXBegin + 1] = far ? 21 : 13; words[BattleWords.EnemyYBegin + 1] = 20;
        return new(new BattleDefinition(0, words), new BattlefieldDefinition(0, new short[8192]), party,
            magics ?? new Dictionary<int, MagicDefinition> { [1] = Magic() }, items, presentActions: presenting);
    }
    private static BattleAiDecision Choose(BattleSession battle, Oracle oracle) => BattleAiRules.Choose(battle.Combatants,
        battle.CurrentActor!, new Dictionary<int, MagicDefinition> { [1] = Magic() }, new Dictionary<int, ItemDefinition>(),
        BattleAiRules.Prelude(battle.Combatants, battle.CurrentActor!), oracle.Draw);
    private static void Drain(BattleSession battle)
    {
        for (var guard = 0; battle.IsPresenting && guard < 100; guard++) battle.AdvancePresentation(TimeSpan.FromSeconds(10));
        Assert.IsFalse(battle.IsPresenting);
    }

    [TestMethod]
    public void NormalTurnConsumesTheNativeThreeDecisionDrawsBeforeChoosingAttack()
    {
        var battle = Ready(); var random = new Oracle(); var choice = Choose(battle, random);
        Assert.AreEqual(BattleAiAction.Attack, choice.Action);
        CollectionAssert.AreEqual(new[] { 10, 10, 50 }, random.Bounds); Assert.AreEqual(662824084u, random.State);
        Assert.AreEqual(new BattleAiPrelude(300, 2, 300, 2), BattleAiRules.Prelude(battle.Combatants, battle.CurrentActor!));
    }

    [TestMethod]
    public void FailedLowHpSelectionPreservesTheOriginalWaitOverwriteBug()
    {
        var tired = Ready(Role(1, (RoleWords.PhysicalPower, 9))); var random = new Oracle();
        Assert.AreEqual(BattleAiAction.Wait, Choose(tired, random).Action); Assert.AreEqual(0, random.Bounds.Count);
        var low = Ready(Role(1, (RoleWords.PhysicalPower, 9), (RoleWords.Hp, 10))); random = new Oracle();
        Assert.AreEqual(BattleAiAction.None, Choose(low, random).Action);
        CollectionAssert.AreEqual(new[] { 10, 10, 50 }, random.Bounds); Assert.AreEqual(662824084u, random.State);
    }

    [TestMethod]
    [DataRow(40, 28, 20)] [DataRow(49, 10, 49)]
    public void SelfMedicalPriorityAllowsFiftyPhysicalAndUsesTheSharedOriginalKernel(int hurt, int hpAfter, int hurtAfter)
    {
        var battle = Ready(Role(1, (RoleWords.Hp, 10), (RoleWords.Hurt, hurt), (RoleWords.PhysicalPower, 50), (RoleWords.Medicine, 20)));
        var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.StepEnemy(), battle.LastError); Assert.AreEqual(BattleAiAction.Medicine, battle.LastAiDecision!.Action);
        // AI's M>H-30 admission is deliberately looser than the actual H<=M+20 treatment kernel.
        Assert.AreEqual(hpAfter, actor.Hp); Assert.AreEqual(hurtAfter, actor.Role[RoleWords.Hurt]); Assert.AreEqual(46, actor.PhysicalPower);
        Assert.AreEqual(1103527590u, battle.RandomState); Assert.AreEqual(1, actor.RewardExperience);
    }

    [TestMethod]
    public void CarriedZeroQuantityMedicinePrecedesRequestAndNeverUsesThePlayerBackpack()
    {
        var item = Item(100, (ItemWords.AddHp, 50)); var items = new Dictionary<int, ItemDefinition> { [100] = item };
        var battle = Ready(Role(1, (RoleWords.Hp, 10), (RoleWords.TakingItemBegin, 100), (RoleWords.TakingCountBegin, 0)),
            Role(3, (RoleWords.Medicine, 80)), items: items); var actor = battle.CurrentActor!;
        var choice = BattleAiRules.LowHp(battle.Combatants, actor, items);
        Assert.AreEqual(BattleAiAction.Item, choice.Action); Assert.AreEqual(0, choice.ItemSlot); Assert.AreEqual(100, choice.ItemId);
        Assert.IsTrue(battle.StepEnemy(), battle.LastError); Assert.AreEqual(BattleAiAction.Item, battle.LastAiDecision!.Action);
        Assert.IsTrue(actor.Hp > 10); Assert.AreEqual(-1, actor.Role[RoleWords.TakingItemBegin]);
        Assert.IsNotNull(battle.LastItemUse);
    }

    [TestMethod]
    public void PoisonResponseKeepsStrictSelfGateAndEnemyAntidoteField()
    {
        var items = new Dictionary<int, ItemDefinition> { [100] = Item(100, (ItemWords.AddPoison, -20)) };
        var battle = Ready(Role(1, (RoleWords.Detoxification, 20), (RoleWords.Poison, 90), (RoleWords.TakingItemBegin, 100)));
        var choice = BattleAiRules.Poisoned(battle.Combatants, battle.CurrentActor!, items);
        Assert.AreEqual(BattleAiAction.Item, choice.Action);
        var noItem = BattleAiRules.Poisoned(battle.Combatants, battle.CurrentActor!,
            new Dictionary<int, ItemDefinition> { [100] = Item(100, (ItemWords.AddUsePoison, -20)) });
        Assert.AreEqual(BattleAiAction.None, noItem.Action);
    }

    [TestMethod]
    public void LowMpDecisionKeepsProbabilityOrderAndSelectsCarriedSourceWithoutQuantityGate()
    {
        var items = new Dictionary<int, ItemDefinition> { [100] = Item(100, (ItemWords.AddMp, 10)) };
        var battle = Ready(Role(1, (RoleWords.Mp, 10), (RoleWords.TakingItemBegin + 2, 100)), items: items); var random = new Oracle();
        var choice = BattleAiRules.Choose(battle.Combatants, battle.CurrentActor!, new Dictionary<int, MagicDefinition> { [1] = Magic() },
            items, BattleAiRules.Prelude(battle.Combatants, battle.CurrentActor!), random.Draw);
        Assert.AreEqual(BattleAiAction.Item, choice.Action); Assert.AreEqual(2, choice.ItemSlot);
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, random.Bounds); Assert.AreEqual(662824084u, random.State);
    }

    [TestMethod]
    public void AllyMedicalAndDetoxTargetsKeepHistoricalProbabilityShortCircuitVectors()
    {
        var battle = Ready(Role(1, (RoleWords.Medicine, 80), (RoleWords.Detoxification, 80)), Role(3, (RoleWords.Hp, 24), (RoleWords.Poison, 35)));
        var random = new Oracle(); var actor = battle.CurrentActor!;
        Assert.AreEqual(BattleAiAction.Medicine, BattleAiRules.MedicineTarget(battle.Combatants, actor, random.Draw).Action);
        Assert.AreEqual(662824084u, random.State); CollectionAssert.AreEqual(new[] { 10, 10, 10 }, random.Bounds);
        random = new Oracle(); Assert.AreEqual(BattleAiAction.Detox, BattleAiRules.DetoxTarget(battle.Combatants, actor, random.Draw).Action);
        Assert.AreEqual(662824084u, random.State);
        random = new Oracle(); var requested = BattleAiRules.MedicineTarget(battle.Combatants, actor, random.Draw, _ => BattleAiAction.RequestMedicine);
        Assert.AreEqual(BattleAiAction.Medicine, requested.Action); Assert.AreEqual(0, random.Bounds.Count);
    }

    [TestMethod]
    [DataRow(40, BattleAiAction.None)] [DataRow(41, BattleAiAction.Detox)]
    public void DetoxTargetFortyIsStrictAndTheDeterministicGateDoesNotDrawAgain(int poison, BattleAiAction expected)
    {
        var battle = Ready(Role(1, (RoleWords.Detoxification, 80)), Role(3, (RoleWords.Poison, poison))); var random = new Oracle(331);
        Assert.AreEqual(expected, BattleAiRules.DetoxTarget(battle.Combatants, battle.CurrentActor!, random.Draw).Action);
        Assert.AreEqual(3382126054u, random.State); Assert.AreEqual(3, random.Bounds.Count);
    }

    [TestMethod]
    public void OffensiveThrowContinuesTheSameItemsPoisonBranchAfterItsHpRollFails()
    {
        var items = new Dictionary<int, ItemDefinition> { [100] = Item(100, (ItemWords.ItemType, 4), (ItemWords.AddHp, -60), (ItemWords.AddPoison, 80)) };
        var battle = Ready(Role(1, (RoleWords.TakingItemBegin + 1, 100)), items: items);
        var bounds = new List<int>(); var rolls = new Queue<int>([49, 9, 2]);
        var choice = BattleAiRules.Offensive(battle.Combatants, battle.CurrentActor!, new Dictionary<int, MagicDefinition> { [1] = Magic() },
            items, BattleAiRules.Prelude(battle.Combatants, battle.CurrentActor!), bound => { bounds.Add(bound); return rolls.Dequeue(); });
        Assert.AreEqual(BattleAiAction.ThrowingWeapon, choice.Action); Assert.AreEqual(1, choice.ItemSlot);
        CollectionAssert.AreEqual(new[] { 50, 10, 10 }, bounds); Assert.AreEqual(0, rolls.Count);
    }

    [TestMethod]
    public void ActualEnemyThrowMovesIntoRangeAndConsumesItsOwnZeroQuantitySource()
    {
        var items = new Dictionary<int, ItemDefinition> { [100] = Item(100, (ItemWords.ItemType, 4), (ItemWords.AddHp, -100), (ItemWords.AddPoison, 30)) };
        var battle = Ready(Role(1, (RoleWords.TakingItemBegin, 100)), items: items); var actor = battle.CurrentActor!;
        var hero = battle.Combatants.Single(person => person.RoleId == 0);
        Assert.IsTrue(battle.StepEnemy(), battle.LastError); Assert.AreEqual(BattleAiAction.ThrowingWeapon, battle.LastAiDecision!.Action);
        Assert.AreEqual(new GridPosition(11, 20), actor.Position); Assert.IsTrue(hero.Hp < 100);
        Assert.AreEqual(30, hero.Role[RoleWords.Poison]); Assert.AreEqual(-1, actor.Role[RoleWords.TakingItemBegin]);
        Assert.AreEqual(100, actor.Role.Definition.Words[RoleWords.TakingItemBegin]);
    }

    [TestMethod]
    public void MedicalRequestApproachesItsHealerThenStillExecutesAutomaticAttack()
    {
        var battle = Ready(Role(1, (RoleWords.Hp, 10)), Role(3, (RoleWords.Medicine, 80))); var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.StepEnemy(), battle.LastError); Assert.AreEqual(BattleAiAction.RequestMedicine, battle.LastAiDecision!.Action);
        Assert.AreNotEqual(new GridPosition(12, 20), actor.Position);
        Assert.IsTrue(battle.LastDamage.Count > 0); Assert.IsTrue(actor.ActionDone); Assert.AreEqual(10, actor.Hp);
    }

    [TestMethod]
    public void ExhaustedMedicalRequesterStillMovesAndExecutesTheNativeAutomaticAttack()
    {
        var battle = Ready(Role(1, (RoleWords.Hp, 10), (RoleWords.PhysicalPower, 4)), Role(3, (RoleWords.Medicine, 80)));
        var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.StepEnemy(), battle.LastError); Assert.AreEqual(BattleAiAction.RequestMedicine, battle.LastAiDecision!.Action);
        Assert.AreNotEqual(new GridPosition(12, 20), actor.Position);
        Assert.IsTrue(battle.LastDamage.Count > 0); Assert.IsTrue(actor.ActionDone); Assert.AreEqual(1, actor.PhysicalPower);
    }

    [TestMethod]
    public void AutomaticPackedSelectionKeepsTheExpensiveMagicInsteadOfReplacingItWithTheAffordableOne()
    {
        var battle = Ready(Role(1, (RoleWords.Mp, 10), (RoleWords.MaximumMp, 10), (RoleWords.MagicIdBegin + 1, 2)),
            magics: new() { [1] = Magic(), [2] = Magic(2, 100) }); var actor = battle.CurrentActor!;
        CollectionAssert.AreEqual(new[] { 0 }, battle.AvailableMagicSlots().ToArray());
        // Seed1's next draw after the three decision draws is packed slot1.
        Assert.IsTrue(battle.StepEnemy(), battle.LastError);
        Assert.AreEqual(0, actor.Role[RoleWords.MagicLevelBegin]); Assert.IsTrue(actor.Role[RoleWords.MagicLevelBegin + 1] > 0);
        Assert.AreEqual(10, actor.Mp); Assert.IsTrue(battle.LastDamage.Count > 0);
    }

    [TestMethod]
    public void InvalidPackedGapStopsSafelyWithoutRepeatingSelectionEveryHostFrame()
    {
        var battle = Ready(Role(1, (RoleWords.MagicIdBegin, 0), (RoleWords.MagicIdBegin + 1, 1))); var actor = battle.CurrentActor!;
        Assert.IsFalse(battle.StepEnemy()); StringAssert.Contains(battle.LastError!, "packed magic");
        Assert.AreSame(actor, battle.CurrentActor); Assert.AreEqual(0, battle.LastDamage.Count); Assert.IsFalse(actor.ActionDone);
        var random = battle.RandomState; Assert.IsFalse(battle.StepEnemy()); Assert.AreEqual(random, battle.RandomState);
    }

    [TestMethod]
    public void EscapeUsesExactActionDistanceAndRetainsTheFirstCoordinateOnEqualScores()
    {
        var battle = Ready(); var actor = battle.CurrentActor!;
        var destination = BattleAiRules.EscapeDestination(battle.Battlefield, battle.Combatants, actor);
        // 34AEC: exact path layer6, maximum sum18; x-outer/y-inner tie picks (12,14).
        Assert.AreEqual(new GridPosition(12, 20), actor.Position); Assert.AreEqual(6, actor.RoundValue);
        Assert.AreEqual(new GridPosition(12, 14), destination);
    }

    [TestMethod]
    [DataRow(75, "strongest", 2)] [DataRow(25, "weakest", 0)]
    public void MoralityStrategiesChooseAttackStatsRatherThanAlwaysNearest(int morality, string strategy, int roleId)
    {
        var battle = Ready(Role(1, (RoleWords.Morality, morality)), hero: Role(0, (RoleWords.Attack, 10)));
        var actor = battle.CurrentActor!;
        var selected = BattleAiRules.AttackTarget(battle.Combatants, actor, _ => 1, _ => 0, actor.Slot);
        Assert.AreEqual(strategy, selected.Strategy); Assert.AreEqual(roleId, battle.Combatants[selected.Slot].RoleId);
    }

    [TestMethod]
    public void PoisonSelectionUsesTheFirstEligibleSlotAndIndependentStaleDistance()
    {
        var battle = Ready(Role(1, (RoleWords.UsePoison, 80), (RoleWords.Iq, 60))); var actor = battle.CurrentActor!;
        var draws = 0; var target = BattleAiRules.PoisonTarget(battle.Combatants, actor, _ => 8, _ => { draws++; return 0; }, 0);
        Assert.AreEqual(0, battle.Combatants[target!.Value].RoleId); Assert.AreEqual(0, draws);
        Assert.ThrowsExactly<ArgumentException>(() => BattleAiRules.PoisonTarget(battle.Combatants, actor, _ => 8, _ => 0, 99));
    }

    [TestMethod]
    public void ValidStaleAttackSlotIsPreservedWhenStrongestSelectorWritesNothing()
    {
        var battle = Ready(Role(1, (RoleWords.Morality, 75)));
        // No eligible opponents: the selected strongest strategy must keep slot0, without nearest fallback.
        var actor = battle.CurrentActor!; var selected = BattleAiRules.AttackTarget([actor], actor, _ => 1, _ => 0, 0);
        Assert.AreEqual(0, selected.Slot); Assert.IsFalse(selected.Written); Assert.AreEqual("strongest", selected.Strategy);
    }

    [TestMethod]
    public void MovePresentationRetainsThePlanAndDoesNotRepeatDecisionOrTargetRolls()
    {
        var battle = Ready(Role(1, (RoleWords.Attack, 500)), far: true, presenting: true); var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.StepEnemy(), battle.LastError); Assert.IsTrue(battle.IsPresenting); Assert.IsTrue(battle.HasPendingAiContinuation);
        Assert.AreEqual(662824084u, battle.RandomState); var decision = battle.LastAiDecision; var frozen = battle.LastAiPrelude;
        Assert.IsFalse(battle.StepEnemy()); Assert.AreEqual(662824084u, battle.RandomState);
        Drain(battle); Assert.AreSame(actor, battle.CurrentActor); Assert.AreEqual(new GridPosition(14, 20), actor.Position);
        Assert.IsTrue(battle.StepEnemy(), battle.LastError); Assert.IsTrue(battle.IsPresenting);
        Assert.AreSame(decision, battle.LastAiDecision); Assert.AreSame(frozen, battle.LastAiPrelude); Assert.IsFalse(battle.HasPendingAiContinuation);
        var oracle = new Oracle(); foreach (var bound in new[] { 10, 10, 50, 20, 20, 2 }) oracle.Draw(bound);
        Assert.AreEqual(oracle.State, battle.RandomState); Assert.IsFalse(battle.StepEnemy());
        Drain(battle); Assert.AreNotSame(actor, battle.CurrentActor);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void OriginalTrainingEnemyCanFinishANativeDecisionWithoutMutatingItsBaseline()
    {
        var library = OriginalLibraryFixture.Library; var catalog = library.Battles!; var definition = catalog.Definitions[0];
        var party = new PartyState(library.GameData.Party!); var initial = party.GetWord(1, RoleWords.MagicLevelBegin);
        var battle = new BattleSession(definition, catalog.Fields[definition.BattlefieldId], party, catalog.Magics, catalog.Items);
        Assert.IsTrue(battle.ConfirmParty([]));
        while (battle.CurrentActor is { IsFriendly: true }) Assert.IsTrue(battle.Rest());
        var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.StepEnemy(), battle.LastError);
        for (var guard = 0; ReferenceEquals(actor, battle.CurrentActor) && guard < 10; guard++)
            Assert.IsTrue(battle.StepEnemy(), battle.LastError);
        Assert.IsNotNull(battle.LastAiDecision); Assert.AreNotSame(actor, battle.CurrentActor);
        Assert.AreEqual(initial, party.GetWord(1, RoleWords.MagicLevelBegin));
    }
}
