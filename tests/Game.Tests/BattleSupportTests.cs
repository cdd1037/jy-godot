using JinYong.Core.Battles;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class BattleSupportTests
{
    private static RoleDefinition Role(int id, params (int Word, int Value)[] changes)
    {
        var words = new int[RoleWords.Count]; words[RoleWords.Id] = id;
        words[RoleWords.Hp] = words[RoleWords.MaximumHp] = words[RoleWords.Mp] = words[RoleWords.MaximumMp] = 100;
        words[RoleWords.PhysicalPower] = 100; words[RoleWords.Speed] = id == 0 ? 90 : id == 2 ? 60 : 30;
        words[RoleWords.Medicine] = words[RoleWords.Detoxification] = words[RoleWords.UsePoison] = 80;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, $"Support fixture {id}", "", words);
    }
    private static PartyState Party(params RoleDefinition[] roles) => new(new PartyDefinition(roles, [0, 2, -1, -1, -1, -1]));
    private static BattleSession Ready(PartyState? party = null, bool blocked = false, Func<int, int>? random = null)
    {
        var words = new int[BattleWords.Count];
        for (var slot = 0; slot < 6; slot++) words[BattleWords.PresetPartyBegin + slot] = words[BattleWords.FixedPartyBegin + slot] = -1;
        for (var slot = 0; slot < 20; slot++) words[BattleWords.EnemyBegin + slot] = -1;
        words[BattleWords.FixedPartyBegin] = 0; words[BattleWords.FixedPartyBegin + 1] = 2;
        words[BattleWords.PartyXBegin] = 10; words[BattleWords.PartyYBegin] = 10;
        words[BattleWords.PartyXBegin + 1] = 11; words[BattleWords.PartyYBegin + 1] = 10;
        words[BattleWords.EnemyBegin] = 1; words[BattleWords.EnemyXBegin] = 12; words[BattleWords.EnemyYBegin] = 10;
        var layers = new short[8192]; if (blocked) layers[4096 + 10 * 64 + 11] = 2;
        return new(new BattleDefinition(0, words), new BattlefieldDefinition(0, layers), party ?? Party(Role(0), Role(1), Role(2)),
            new Dictionary<int, MagicDefinition>(), random: random);
    }

    [TestMethod]
    [DataRow(BattleSupportAction.UsePoison, 10, 80, false)]
    [DataRow(BattleSupportAction.UsePoison, 11, 19, false)]
    [DataRow(BattleSupportAction.UsePoison, 11, 20, true)]
    [DataRow(BattleSupportAction.Detox, 50, 80, false)]
    [DataRow(BattleSupportAction.Detox, 51, 19, false)]
    [DataRow(BattleSupportAction.Detox, 51, 20, true)]
    [DataRow(BattleSupportAction.Medicine, 50, 80, false)]
    [DataRow(BattleSupportAction.Medicine, 51, 20, true)]
    public void AvailabilityKeepsOriginalStrictPhysicalAndSkillThresholds(BattleSupportAction action, int physical, int skill, bool available)
    {
        var party = Party(Role(0, (RoleWords.PhysicalPower, physical), (BattleSupportRules.SkillWord(action), skill)), Role(1), Role(2));
        var battle = Ready(party);
        Assert.AreEqual(available, battle.CanUseSupport(action));
        Assert.AreEqual(available, battle.CanTargetSupport(action, new(10, 10)));
        if (!available)
            Assert.IsFalse(action switch { BattleSupportAction.UsePoison => battle.UsePoison(new(10, 10)),
                BattleSupportAction.Detox => battle.Detox(new(10, 10)), _ => battle.Medicine(new(10, 10)) });
        Assert.AreEqual(physical, party.GetWord(0, RoleWords.PhysicalPower)); Assert.AreEqual(1u, battle.RandomState);
    }

    [TestMethod]
    public void RangeUsesUncappedSignedSkillAndTheUpperLayerTargetingMap()
    {
        var role = Party(Role(0), Role(1), Role(2)).Roles[0];
        Assert.AreEqual(6, BattleSupportRules.Range(role, BattleSupportAction.UsePoison));
        var party = Party(Role(0, (RoleWords.UsePoison, 150)), Role(1), Role(2));
        Assert.AreEqual(11, Ready(party).SupportRange(BattleSupportAction.UsePoison)); // No NG+ cap to 100.
        party.SetWord(0, RoleWords.UsePoison, -15);
        Assert.AreEqual(0, BattleSupportRules.Range(party.Roles[0], BattleSupportAction.UsePoison));
        var narrow = Party(Role(0, (RoleWords.UsePoison, 20)), Role(1), Role(2));
        var battle = Ready(narrow, blocked: true);
        Assert.IsFalse(battle.CanTargetSupport(BattleSupportAction.UsePoison, new(12, 10))); // Manhattan 2; detour is 4.
        Assert.IsFalse(battle.UsePoison(new(12, 10))); Assert.IsFalse(battle.Detox(new(64, 10)));
        Assert.IsFalse(battle.Medicine(new(-1, 10))); Assert.AreEqual(100, narrow.GetWord(0, RoleWords.PhysicalPower));
        Assert.AreEqual(0, battle.CurrentActor!.RewardExperience); Assert.IsFalse(battle.CurrentActor.ActionDone);
        Assert.AreEqual(1u, battle.RandomState); Assert.IsNull(battle.LastSupportResult);
    }

    [TestMethod]
    public void PoisonUsesDifferenceDividedByFourWithoutHpOverflowOrRandomness()
    {
        var party = Party(Role(0), Role(1, (RoleWords.AntiPoison, 20), (RoleWords.Poison, 10)), Role(2));
        var battle = Ready(party); var actor = battle.CurrentActor!; var enemy = battle.Combatants.Single(person => !person.IsFriendly);
        Assert.IsTrue(battle.UsePoison(enemy.Position), battle.LastError);
        Assert.AreEqual(25, enemy.Role[RoleWords.Poison]); Assert.AreEqual(100, enemy.Hp);
        Assert.AreEqual(10, party.GetWord(1, RoleWords.Poison)); // Independent enemy baseline.
        Assert.AreEqual(98, party.GetWord(0, RoleWords.PhysicalPower)); Assert.AreEqual(1, actor.RewardExperience);
        Assert.IsTrue(actor.ActionDone); Assert.AreEqual(2, battle.CurrentActor!.RoleId);
        Assert.AreEqual(15, battle.LastSupportResult!.Amount); Assert.AreEqual(enemy.Slot, battle.LastSupportResult.TargetSlot);
        Assert.AreEqual(Direction.Right, actor.Facing); Assert.AreEqual(1u, battle.RandomState); Assert.AreEqual(0, battle.LastDamage.Count);
        var capped = Party(Role(0), Role(1, (RoleWords.Poison, 98)), Role(2));
        var result = BattleSupportRules.UsePoison(capped.Roles[0], capped.Roles[1]);
        Assert.AreEqual(1, result.Amount); Assert.AreEqual(99, result.Poison);
        Assert.AreEqual(98, capped.GetWord(1, RoleWords.Poison)); // Pure kernel.
    }

    [TestMethod]
    public void DetoxMatchesHistoricalSeedOneVectorAndModifiesTheSharedPatientRole()
    {
        var party = Party(Role(0), Role(1), Role(2, (RoleWords.Poison, 90)));
        var battle = Ready(party); var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.Detox(new(11, 10)), battle.LastError);
        // Evidence 0x39DA3: ability80, poison90, rolls[8,8], amount26, final poison64.
        Assert.AreEqual(64, party.GetWord(2, RoleWords.Poison)); Assert.AreEqual(26, battle.LastSupportResult!.Amount);
        Assert.AreEqual(2524885223u, battle.RandomState); Assert.AreEqual(98, party.GetWord(0, RoleWords.PhysicalPower));
        Assert.AreSame(party.Roles[2], battle.CurrentActor!.Role); Assert.IsTrue(actor.ActionDone);
    }

    [TestMethod]
    [DataRow(40, 6)] [DataRow(41, 0)]
    public void DetoxStrictThresholdStillConsumesBothRandomDraws(int poison, int expectedAmount)
    {
        var party = Party(Role(0, (RoleWords.Detoxification, 20)), Role(1), Role(2, (RoleWords.Poison, poison)));
        var bounds = new List<int>(); var battle = Ready(party, random: bound => { bounds.Add(bound); return 8; });
        Assert.IsTrue(battle.Detox(new(11, 10)));
        Assert.AreEqual(expectedAmount, battle.LastSupportResult!.Amount); Assert.AreEqual(poison - expectedAmount, party.GetWord(2, RoleWords.Poison));
        CollectionAssert.AreEqual(new[] { 10, 10 }, bounds); Assert.AreEqual(98, party.GetWord(0, RoleWords.PhysicalPower));
    }

    [TestMethod]
    [DataRow(25, 67)] [DataRow(26, 63)] [DataRow(50, 63)] [DataRow(51, 56)] [DataRow(75, 56)] [DataRow(76, 43)]
    public void MedicineKeepsTheOriginalFourInjuryBandsAndTwoSeparateCosts(int hurt, int expectedAmount)
    {
        var party = Party(Role(0), Role(1), Role(2, (RoleWords.Hp, 10), (RoleWords.Hurt, hurt)));
        var battle = Ready(party); var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.Medicine(new(11, 10)), battle.LastError);
        Assert.AreEqual(expectedAmount, battle.LastSupportResult!.Amount); Assert.AreEqual(10 + expectedAmount, party.GetWord(2, RoleWords.Hp));
        Assert.AreEqual(0, party.GetWord(2, RoleWords.Hurt)); Assert.AreEqual(96, party.GetWord(0, RoleWords.PhysicalPower));
        Assert.AreEqual(1103527590u, battle.RandomState); Assert.AreEqual(1, actor.RewardExperience); Assert.IsTrue(actor.ActionDone);
    }

    [TestMethod]
    public void FullHpStillTreatsInjuryWhileIneligibleInjuryConsumesItsOneRollAndBothCosts()
    {
        var party = Party(Role(0), Role(1), Role(2, (RoleWords.Hurt, 50))); var battle = Ready(party);
        Assert.IsTrue(battle.Medicine(new(11, 10))); Assert.AreEqual(0, battle.LastSupportResult!.Amount);
        Assert.AreEqual(100, party.GetWord(2, RoleWords.Hp)); Assert.AreEqual(0, party.GetWord(2, RoleWords.Hurt));
        var failed = Party(Role(0), Role(1), Role(2, (RoleWords.Hp, 20), (RoleWords.Hurt, 101))); battle = Ready(failed);
        Assert.IsTrue(battle.Medicine(new(11, 10))); Assert.AreEqual(0, battle.LastSupportResult!.Amount);
        Assert.AreEqual(20, failed.GetWord(2, RoleWords.Hp)); Assert.AreEqual(101, failed.GetWord(2, RoleWords.Hurt));
        Assert.AreEqual(96, failed.GetWord(0, RoleWords.PhysicalPower)); Assert.AreEqual(1103527590u, battle.RandomState);
    }

    [TestMethod]
    [DataRow(100, 43, 20)] [DataRow(101, 0, 101)]
    public void MedicineKeepsTheStrictInjuryAdmissionBoundary(int hurt, int amount, int remainingHurt)
    {
        var party = Party(Role(0), Role(1), Role(2, (RoleWords.Hp, 20), (RoleWords.Hurt, hurt)));
        var battle = Ready(party); Assert.IsTrue(battle.Medicine(new(11, 10)));
        Assert.AreEqual(amount, battle.LastSupportResult!.Amount); Assert.AreEqual(20 + amount, party.GetWord(2, RoleWords.Hp));
        Assert.AreEqual(remainingHurt, party.GetWord(2, RoleWords.Hurt)); Assert.AreEqual(96, party.GetWord(0, RoleWords.PhysicalPower));
        Assert.AreEqual(1103527590u, battle.RandomState);
    }

    [TestMethod]
    public void SelfMedicinePreservesFacingAndSharesHpHurtAndPhysicalWrites()
    {
        var party = Party(Role(0, (RoleWords.Hp, 20), (RoleWords.Hurt, 50)), Role(1), Role(2));
        var battle = Ready(party); var actor = battle.CurrentActor!; var facing = actor.Facing;
        Assert.IsTrue(battle.Medicine(actor.Position));
        Assert.AreEqual(83, party.GetWord(0, RoleWords.Hp)); Assert.AreEqual(0, party.GetWord(0, RoleWords.Hurt));
        Assert.AreEqual(96, party.GetWord(0, RoleWords.PhysicalPower)); Assert.AreEqual(facing, actor.Facing);
        Assert.AreEqual(actor.Slot, battle.LastSupportResult!.TargetSlot);
    }

    [TestMethod]
    [DataRow(BattleSupportAction.UsePoison, false)] [DataRow(BattleSupportAction.Detox, false)] [DataRow(BattleSupportAction.Medicine, false)]
    [DataRow(BattleSupportAction.UsePoison, true)] [DataRow(BattleSupportAction.Detox, true)] [DataRow(BattleSupportAction.Medicine, true)]
    public void EmptyAndWrongSideConfirmationsEndTheActionWithoutKernelOrRandomness(BattleSupportAction action, bool wrongSide)
    {
        var party = Party(Role(0), Role(1), Role(2)); var battle = Ready(party); var actor = battle.CurrentActor!;
        var target = wrongSide ? new GridPosition(action == BattleSupportAction.UsePoison ? 11 : 12, 10) : new(10, 11);
        Assert.IsTrue(battle.CanTargetSupport(action, target));
        var success = action switch { BattleSupportAction.UsePoison => battle.UsePoison(target), BattleSupportAction.Detox => battle.Detox(target), _ => battle.Medicine(target) };
        Assert.IsTrue(success); Assert.IsTrue(actor.ActionDone); Assert.AreEqual(1, actor.RewardExperience);
        Assert.AreEqual(98, actor.PhysicalPower); Assert.AreEqual(1u, battle.RandomState);
        Assert.AreEqual(0, battle.LastSupportResult!.Amount); Assert.IsNull(battle.LastSupportResult.TargetSlot);
        Assert.AreEqual(100, party.GetWord(2, RoleWords.Hp)); Assert.AreEqual(0, party.GetWord(2, RoleWords.Poison));
    }

    [TestMethod]
    public void EnemyCanUseTheSameActionWithoutMutatingItsSharedBaselineRole()
    {
        var party = Party(Role(0), Role(1, (RoleWords.Speed, 120)), Role(2)); var battle = Ready(party);
        var enemy = battle.CurrentActor!; Assert.IsFalse(enemy.IsFriendly);
        Assert.IsTrue(battle.UsePoison(new(10, 10))); Assert.AreEqual(20, party.GetWord(0, RoleWords.Poison));
        Assert.AreEqual(98, enemy.PhysicalPower); Assert.AreEqual(100, party.GetWord(1, RoleWords.PhysicalPower));
        Assert.AreEqual(BattleOutcome.Ongoing, battle.Outcome);
    }

    [TestMethod]
    public void PureMedicineKernelKeepsItsDistinctFiftyPhysicalBoundary()
    {
        var party = Party(Role(0, (RoleWords.PhysicalPower, 49)), Role(1), Role(2, (RoleWords.Hp, 20)));
        var draws = 0; int Random(int bound) { draws++; Assert.AreEqual(5, bound); return 3; }
        var noTreatment = BattleSupportRules.Medicine(party.Roles[0], party.Roles[2], Random);
        Assert.AreEqual(0, draws); Assert.AreEqual(20, noTreatment.Hp); Assert.AreEqual(49, noTreatment.PhysicalPower);
        party.SetWord(0, RoleWords.PhysicalPower, 50);
        var treated = BattleSupportRules.Medicine(party.Roles[0], party.Roles[2], Random);
        Assert.AreEqual(1, draws); Assert.AreEqual(87, treated.Hp); Assert.AreEqual(48, treated.PhysicalPower);
        Assert.AreEqual(20, party.GetWord(2, RoleWords.Hp)); Assert.AreEqual(50, party.GetWord(0, RoleWords.PhysicalPower));
    }
}
