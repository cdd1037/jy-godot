using JinYong.Core.Battles;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class BattleSettlementTests
{
    private static ProgressionDefinition Rules() => new(Enumerable.Range(0, 30).Select(level => level * 1000).ToArray(), 999, 999, 7, 15, 1, 2, 8, 10);
    private static RoleDefinition Role(int id, int hp = 100, int experience = 0, int level = 30, int practice = -1)
    {
        var words = new int[91]; words[RoleWords.Id] = id; words[RoleWords.Hp] = hp; words[RoleWords.MaximumHp] = 100;
        words[RoleWords.Mp] = 30; words[RoleWords.MaximumMp] = 80; words[RoleWords.Level] = level;
        words[RoleWords.Experience] = unchecked((short)experience); words[RoleWords.PracticeItem] = practice;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = -1;
        words[RoleWords.Speed] = 30; words[RoleWords.PhysicalPower] = 5;
        for (var slot = 0; slot < 4; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        return new(id, $"Role {id}", "", words);
    }
    private static BattleSession Battle(PartyState party, int experience = 0, bool secondFriendly = false)
    {
        var words = new int[93]; words[7] = experience;
        for (var slot = 0; slot < 6; slot++)
        {
            words[BattleWords.PresetPartyBegin + slot] = words[BattleWords.FixedPartyBegin + slot] = -1;
            words[BattleWords.PartyXBegin + slot] = 20 + slot; words[BattleWords.PartyYBegin + slot] = 20;
        }
        words[BattleWords.FixedPartyBegin] = 0;
        if (secondFriendly) words[BattleWords.FixedPartyBegin + 1] = 2;
        for (var slot = 0; slot < 20; slot++) words[BattleWords.EnemyBegin + slot] = -1;
        words[BattleWords.EnemyBegin] = 1; words[BattleWords.EnemyXBegin] = 30; words[BattleWords.EnemyYBegin] = 30;
        return new(new(0, words), new(0, new short[8192]), party, new Dictionary<int, MagicDefinition>());
    }
    private static PartyState Party(RoleDefinition hero, RoleDefinition? ally = null) => new(new(
        ally is null ? [hero, Role(1)] : [hero, Role(1), ally], ally is null ? [0, -1, -1, -1, -1, -1] : [0, 2, -1, -1, -1, -1]));

    [TestMethod]
    public void VictorySharesOnlyWithLivingFriendlyAndRestoresDefeatedPartyFloor()
    {
        var party = Party(Role(0, 10), Role(2, 0));
        var battle = Battle(party, 150, secondFriendly: true);
        var result = BattleSettlement.Settle(battle.Definition, battle.Combatants, true, 0, Rules(), new Dictionary<int, ItemDefinition>(),
            _ => throw new AssertFailedException("No progression RNG should run at level 30."), new Dictionary<int, int>(), new HashSet<int>());
        Assert.AreEqual(150, result.SharedExperience);
        Assert.AreEqual(150, party.Roles[0][RoleWords.Experience]);
        Assert.AreEqual(120, party.Roles[0][RoleWords.ItemExperience]);
        Assert.AreEqual(0, party.Roles[2][RoleWords.Experience]);
        Assert.AreEqual(20, party.Roles[0][RoleWords.Hp]); Assert.AreEqual(5, party.Roles[0][RoleWords.PhysicalPower]);
        Assert.AreEqual(20, party.Roles[2][RoleWords.Hp]); Assert.AreEqual(10, party.Roles[2][RoleWords.PhysicalPower]);
        Assert.AreEqual(80, battle.Combatants.Single(actor => actor.Side == 1).Role[RoleWords.Mp]);
        Assert.AreEqual(30, party.Roles[1][RoleWords.Mp]); // enemy baseline is untouched
        Assert.HasCount(2, result.Notices);
    }

    [TestMethod]
    [DataRow(0, -1, 60000, 39320)]
    [DataRow(65000, 1000, 464, 800)]
    [DataRow(59000, 2000, 60000, 1600)]
    public void OriginalExperienceWrapsLowWordBeforeUnsignedCap(int start, int shared, int expected, int training)
    {
        var party = Party(Role(0, experience: start)); var battle = Battle(party, shared);
        BattleSettlement.Settle(battle.Definition, battle.Combatants, true, 0, Rules(), new Dictionary<int, ItemDefinition>(),
            _ => 0, new Dictionary<int, int>(), new HashSet<int>());
        Assert.AreEqual(expected, party.Roles[0][RoleWords.Experience]);
        Assert.AreEqual(training, party.Roles[0][RoleWords.ItemExperience]);
    }

    [TestMethod]
    [DataRow(0, 1, 0)] [DataRow(9, 1, 0)] [DataRow(1, 2, 3)]
    public void GetExperienceGateIsExactlyOneAndDoesNotReplaceTheExperienceWrite(int getExp, int level, int draws)
    {
        var party = Party(Role(0, experience: 1500, level: 1)); var battle = Battle(party);
        var calls = 0;
        var result = BattleSettlement.Settle(battle.Definition, battle.Combatants, false, getExp, Rules(), new Dictionary<int, ItemDefinition>(),
            _ => { calls++; return 0; }, new Dictionary<int, int>(), new HashSet<int>());
        Assert.AreEqual(1500, party.Roles[0][RoleWords.Experience]);
        Assert.AreEqual(level, party.Roles[0][RoleWords.Level]); Assert.AreEqual(draws, calls);
        Assert.AreEqual(getExp == 1, result.Roles.Single(role => role.Side == 0).ExperienceMessageRequired);
    }

    [TestMethod]
    public void PracticeUpdatesEveryDuplicateMagicAndPreservesAlreadyCappedSlots()
    {
        var party = Party(Role(0, practice: 0));
        party.SetWord(0, RoleWords.ItemExperience, 1000);
        for (var slot = 0; slot < 3; slot++) party.SetWord(0, RoleWords.MagicIdBegin + slot, 1);
        party.SetWord(0, RoleWords.MagicLevelBegin, 199); party.SetWord(0, RoleWords.MagicLevelBegin + 1, 899); party.SetWord(0, RoleWords.MagicLevelBegin + 2, 898);
        var manual = new int[95]; manual[ItemWords.MagicId] = 1; manual[ItemWords.NeedExperience] = 10; manual[ItemWords.AddAttack] = 5;
        var battle = Battle(party);
        var result = BattleSettlement.Settle(battle.Definition, battle.Combatants, true, 0, Rules(), new Dictionary<int, ItemDefinition> { [0] = new(0, "Manual", manual) },
            _ => throw new AssertFailedException("Practice alone consumes no RNG."), new Dictionary<int, int>(), new HashSet<int>());
        Assert.AreEqual(299, party.Roles[0][RoleWords.MagicLevelBegin]); Assert.AreEqual(899, party.Roles[0][RoleWords.MagicLevelBegin + 1]);
        Assert.AreEqual(998, party.Roles[0][RoleWords.MagicLevelBegin + 2]);
        CollectionAssert.AreEqual(new[] { 0, 2 }, result.Roles.Single(role => role.Side == 0).IncreasedMagicSlots.ToArray());
        Assert.AreEqual(0, party.Roles[0][RoleWords.ItemExperience]); Assert.AreEqual(5, party.Roles[0][RoleWords.Attack]);
    }

    [TestMethod]
    public void CraftingUsesRejectionSamplingThenExistingProductQuantityDraw()
    {
        var party = Party(Role(0, practice: 0)); party.SetWord(0, RoleWords.MakeItemExperience, 1000);
        var manual = new int[95]; manual[ItemWords.MagicId] = -1; manual[ItemWords.NeedExperience] = 1000;
        manual[ItemWords.NeedMakeItemExperience] = 10; manual[ItemWords.NeedMaterial] = 1;
        for (var slot = 0; slot < 5; slot++) manual[ItemWords.MakeItemBegin + slot] = -1;
        manual[ItemWords.MakeItemBegin] = 2; manual[ItemWords.MakeItemCountBegin] = 2;
        ItemDefinition Item(int id) { var words = new int[95]; words[0] = id; return new(id, $"Item {id}", words); }
        var items = new Dictionary<int, ItemDefinition> { [0] = new(0, "Manual", manual), [1] = Item(1), [2] = Item(2) };
        var sequence = new Queue<(int Bound, int Value)>([(5, 3), (5, 3), (5, 3), (5, 0), (3, 1)]);
        var bag = new Dictionary<int, int> { [1] = 3, [2] = 4 }; var ids = bag.Keys.ToHashSet(); var battle = Battle(party);
        var result = BattleSettlement.Settle(battle.Definition, battle.Combatants, true, 0, Rules(), items, bound =>
            { var next = sequence.Dequeue(); Assert.AreEqual(next.Bound, bound); return next.Value; }, bag, ids);
        Assert.HasCount(0, sequence); Assert.AreEqual(1, bag[1]); Assert.AreEqual(6, bag[2]);
        Assert.AreEqual(0, party.Roles[0][RoleWords.MakeItemExperience]);
        Assert.AreEqual(2, result.Roles.Single(role => role.Side == 0).CraftedCount);
    }
}
