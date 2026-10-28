using JinYong.Core.Battles;
using JinYong.Core.Items;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Independent definitions and original DOS machine vectors, not NG+ kernels.
[TestClass]
public sealed class BattleItemTests
{
    [TestMethod]
    public void SharedMedicineMatchesOriginalItem19VectorWithoutNgPlusHpScaling()
    {
        var party = Party(Role(0, (RoleWords.MaximumHp, 200), (RoleWords.Hurt, 40), (RoleWords.Poison, 50),
            (RoleWords.PhysicalPower, 30), (RoleWords.Mp, 10), (RoleWords.HiddenWeapon, 20)));
        var medicine = Item(19, 3, (ItemWords.AddHp, 5000), (ItemWords.AddPoison, -100),
            (ItemWords.AddPhysicalPower, 100), (ItemWords.AddMp, 5000));
        var random = new NativeRandom(); var role = party.Roles[0];
        var result = ItemUseRules.Calculate(role, role, medicine, random.Next);
        Assert.AreEqual(100, role[RoleWords.Hp]); // calculation does not commit
        CollectionAssert.AreEqual(new[] { 10, 5, 5 }, random.Bounds);
        Assert.AreEqual(662824084u, random.State);
        CollectionAssert.AreEqual(new[] { 100, 0, -50, 70, 0, 90, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, result.Deltas.ToArray());
        ItemUseRules.Apply(role, result);
        Assert.AreEqual(200, role[RoleWords.Hp]); Assert.AreEqual(0, role[RoleWords.Hurt]);
        Assert.AreEqual(0, role[RoleWords.Poison]); Assert.AreEqual(100, role[RoleWords.PhysicalPower]); Assert.AreEqual(100, role[RoleWords.Mp]);
        party.SetWord(0, RoleWords.Hp, 50); party.SetWord(0, RoleWords.MaximumHp, 200);
        var fixedHealing = ItemUseRules.Calculate(role, role, Item(1, 3, (ItemWords.AddHp, 20)), new NativeRandom().Next);
        Assert.AreEqual(78, fixedHealing.Words[RoleWords.Hp]); // +28, never *200/999
    }

    [TestMethod]
    public void HpFallbackBranchesConsumeOnlyTheirOriginalConditionalDraws()
    {
        var party = Party(Role(0, (RoleWords.MaximumHp, 200), (RoleWords.Hurt, 40), (RoleWords.HiddenWeapon, 20)));
        var random = new NativeRandom();
        var heal = ItemUseRules.Calculate(party.Roles[0], party.Roles[0], Item(1, 3, (ItemWords.AddHp, 1)), random.Next);
        Assert.AreEqual(108, heal.Words[RoleWords.Hp]); CollectionAssert.AreEqual(new[] { 10, 5 }, random.Bounds);
        party.SetWord(0, RoleWords.Hurt, 0); random = new NativeRandom();
        var harm = ItemUseRules.Calculate(party.Roles[0], party.Roles[0], Item(1, 3, (ItemWords.AddHp, -1)), random.Next);
        Assert.AreEqual(78, harm.Words[RoleWords.Hp]); Assert.AreEqual(2, harm.Words[RoleWords.Hurt]);
        CollectionAssert.AreEqual(new[] { 10, 5 }, random.Bounds);
    }

    [TestMethod]
    public void OriginalWordCapsAbilityWrapAndDisplayOnlyBugsRemainExplicit()
    {
        var party = Party(Role(0, (RoleWords.MaximumHp, 900), (RoleWords.MaximumMp, 940),
            (RoleWords.Speed, 32760), (RoleWords.Morality, 50), (RoleWords.AttackTwice, 4)));
        var random = new NativeRandom(); var role = party.Roles[0];
        var result = ItemUseRules.Calculate(role, role, Item(1, 3, (ItemWords.AddMaximumHp, 200),
            (ItemWords.AddMaximumMp, 100), (ItemWords.AddSpeed, 20), (ItemWords.AddMorality, 10), (ItemWords.AddAttackTwice, 1)), random.Next);
        ItemUseRules.Apply(role, result);
        Assert.AreEqual(999, role[RoleWords.MaximumHp]); Assert.AreEqual(999, role[RoleWords.MaximumMp]);
        Assert.AreEqual(-32756, role[RoleWords.Speed]); Assert.AreEqual(50, role[RoleWords.Morality]); Assert.AreEqual(4, role[RoleWords.AttackTwice]);
        Assert.AreEqual(10, result.Deltas[20]); Assert.AreEqual(1, result.Deltas[21]); Assert.IsTrue(result.HasEffect); Assert.HasCount(0, random.Bounds);
        party.SetWord(0, RoleWords.MaximumHp, 32760);
        Assert.AreEqual(0, ItemUseRules.Calculate(role, role, Item(1, 3, (ItemWords.AddMaximumHp, 20)), random.Next).Words[RoleWords.MaximumHp]);
    }

    [TestMethod]
    public void MedicineConsumesSharedInventoryExactlyOnceAndRejectsWrongTypesOrMissingSource()
    {
        var inventory = new Inventory((1, 2), (2, 1)); var random = new NativeRandom();
        var party = Party(Role(0, (RoleWords.Hp, 50)), Role(1));
        var battle = Ready(party, [Item(1, 3, (ItemWords.AddHp, 20)), Item(2, 4)], inventory, random);
        Assert.IsFalse(battle.UseBattleItem(999)); Assert.IsFalse(battle.UseBattleItem(2));
        Assert.AreEqual(0, inventory.Consumes); Assert.HasCount(0, random.Bounds);
        Assert.IsTrue(battle.UseBattleItem(1)); Assert.AreEqual(78, party.Roles[0][RoleWords.Hp]);
        Assert.AreEqual(1, inventory.Items[1]); Assert.AreEqual(1, inventory.Consumes);
        Assert.IsTrue(battle.Combatants.Single(actor => actor.IsFriendly).ActionDone);
        Assert.IsFalse(battle.UseBattleItem(1)); // enemy cannot borrow party inventory
        Assert.AreEqual(1, inventory.Consumes);
    }

    [TestMethod]
    public void NoEffectManualMedicineEndsActionWithoutConsumptionOrRandomDraws()
    {
        var inventory = new Inventory((1, 1)); var random = new NativeRandom();
        var battle = Ready(Party(Role(0), Role(1)), [Item(1, 3)], inventory, random);
        Assert.IsTrue(battle.UseBattleItem(1)); Assert.IsFalse(battle.LastItemUse!.HasEffect);
        Assert.AreEqual(0, inventory.Consumes); Assert.HasCount(0, random.Bounds);
        Assert.IsFalse(battle.CurrentActor!.IsFriendly);
    }

    [TestMethod]
    public void EnemyMedicineConsumesAndCompactsOnlyItsActualCarriedSlotsEvenAtZeroEffect()
    {
        var inventory = new Inventory((1, 5)); var random = new NativeRandom();
        var enemy = Role(1, (RoleWords.Speed, 90), (RoleWords.TakingItemBegin, 1), (RoleWords.TakingCountBegin, 1),
            (RoleWords.TakingItemBegin + 1, 2), (RoleWords.TakingCountBegin + 1, 3));
        var party = Party(Role(0), enemy); var battle = Ready(party, [Item(1, 3), Item(2, 4)], inventory, random);
        var actor = battle.CurrentActor!; Assert.IsFalse(actor.IsFriendly);
        CollectionAssert.AreEqual(new[] { 1, 2 }, battle.AvailableBattleItems().ToArray());
        Assert.IsTrue(battle.UseBattleItem(1));
        Assert.AreEqual(2, actor.Role[RoleWords.TakingItemBegin]); Assert.AreEqual(3, actor.Role[RoleWords.TakingCountBegin]);
        Assert.AreEqual(-1, actor.Role[RoleWords.TakingItemBegin + 3]); Assert.AreEqual(0, actor.Role[RoleWords.TakingCountBegin + 3]);
        Assert.AreEqual(1, party.Roles[1][RoleWords.TakingItemBegin]); // immutable enemy baseline / party-role copy untouched
        Assert.AreEqual(5, inventory.Items[1]); Assert.AreEqual(0, inventory.Consumes);
    }

    [TestMethod]
    public void ManualThrowMatchesOriginalDamagePoisonRangeAndSharedRandomStream()
    {
        var inventory = new Inventory((102, 2)); var random = new NativeRandom();
        var party = Party(Role(0, (RoleWords.HiddenWeapon, 20)), Role(1, (RoleWords.Hurt, 40), (RoleWords.Poison, 10), (RoleWords.AntiPoison, 5)));
        var battle = Ready(party, [Item(102, 4, (ItemWords.AddHp, -40), (ItemWords.AddPoison, 40), (ItemWords.HiddenWeaponEffectId, 30))], inventory, random);
        Assert.IsTrue(battle.CanTargetThrownItem(102, new(12, 10)));
        Assert.IsTrue(battle.ThrowItem(102, new(12, 10)));
        var target = battle.Combatants.Single(actor => !actor.IsFriendly);
        Assert.AreEqual(79, target.Hp); Assert.AreEqual(45, target.Role[RoleWords.Hurt]); Assert.AreEqual(12, target.Role[RoleWords.Poison]);
        Assert.AreEqual(21, battle.LastDamage.Single().Damage); Assert.AreEqual(30, battle.LastThrownEffectId);
        Assert.AreEqual(100, party.Roles[0][RoleWords.PhysicalPower]); Assert.AreEqual(0, battle.Combatants.Single(actor => actor.IsFriendly).RewardExperience);
        Assert.AreEqual(1, inventory.Items[102]); CollectionAssert.AreEqual(new[] { 5 }, random.Bounds); Assert.AreEqual(1103527590u, random.State);
        battle.Rest(); // next legal action advances the very same supplied stream
        Assert.AreNotEqual(1103527590u, random.State);
    }

    [TestMethod]
    public void EnemyThrowUsesCarriedSourceAndOriginalAiPoisonWithoutResistanceOrExtraDraws()
    {
        var inventory = new Inventory((102, 4)); var random = new NativeRandom();
        var party = Party(Role(0, (RoleWords.Hurt, 40), (RoleWords.Poison, 10), (RoleWords.AntiPoison, 100)),
            Role(1, (RoleWords.Speed, 90), (RoleWords.HiddenWeapon, 20), (RoleWords.TakingItemBegin, 102), (RoleWords.TakingCountBegin, 1)));
        var battle = Ready(party, [Item(102, 4, (ItemWords.AddHp, -40), (ItemWords.AddPoison, 40))], inventory, random);
        var actor = battle.CurrentActor!; var oldFacing = actor.Facing;
        Assert.IsTrue(battle.ThrowItem(102, new(10, 10)));
        Assert.AreEqual(79, party.Roles[0][RoleWords.Hp]); Assert.AreEqual(50, party.Roles[0][RoleWords.Poison]);
        Assert.AreEqual(oldFacing, actor.Facing); Assert.AreEqual(-1, actor.Role[RoleWords.TakingItemBegin]);
        Assert.AreEqual(4, inventory.Items[102]); Assert.AreEqual(0, inventory.Consumes); CollectionAssert.AreEqual(new[] { 5 }, random.Bounds);
    }

    [TestMethod]
    public void InvalidTargetsAndEmptyOrWrongInventoryDoNotConsumeOrDrawRandom()
    {
        var inventory = new Inventory((1, 2), (2, 0), (3, 1)); var random = new NativeRandom();
        var battle = Ready(Party(Role(0, (RoleWords.HiddenWeapon, 20)), Role(1)), [Item(1, 4), Item(2, 4), Item(3, 3)], inventory, random);
        foreach (var (item, target) in new[] { (1, new GridPosition(10, 10)), (1, new GridPosition(11, 10)),
            (1, new GridPosition(-1, 10)), (1, new GridPosition(63, 63)), (2, new GridPosition(12, 10)), (3, new GridPosition(12, 10)) })
            Assert.IsFalse(battle.ThrowItem(item, target));
        Assert.AreEqual(0, inventory.Consumes); Assert.HasCount(0, random.Bounds); Assert.AreEqual(100, battle.Combatants[1].Hp);
    }

    [TestMethod]
    public void ManualZeroPoisonStillDrawsTwiceAndThrowingRandomBaseWrapsAsSignedWord()
    {
        var inventory = new Inventory((1, 1)); var random = new NativeRandom(7); // first bounded(5) is 4
        var party = Party(Role(0, (RoleWords.HiddenWeapon, 0)), Role(1, (RoleWords.Hurt, 67), (RoleWords.MaximumHp, 20000)));
        var battle = Ready(party, [Item(1, 4, (ItemWords.AddHp, short.MinValue), (ItemWords.AddPoison, 0))], inventory, random, enemyX: 11);
        Assert.IsTrue(battle.ThrowItem(1, new(11, 10)));
        var target = battle.Combatants.Single(actor => !actor.IsFriendly);
        Assert.AreEqual(11021, target.Hp); Assert.AreEqual(0, target.Role[RoleWords.Hurt]); Assert.AreEqual(10921, battle.LastDamage[0].Damage);
        CollectionAssert.AreEqual(new[] { 5, 5, 5 }, random.Bounds);
    }

    [TestMethod]
    public void OccupiedEnemyOutsideThrowRangeCannotConsumeEvenWhenTheItemExists()
    {
        var inventory = new Inventory((1, 2)); var random = new NativeRandom();
        var battle = Ready(Party(Role(0, (RoleWords.HiddenWeapon, 20)), Role(1)), [Item(1, 4)], inventory, random, enemyX: 13);
        Assert.IsFalse(battle.CanTargetThrownItem(1, new(13, 10)));
        Assert.IsFalse(battle.ThrowItem(1, new(13, 10)));
        Assert.AreEqual(2, inventory.Items[1]); Assert.AreEqual(0, inventory.Consumes); Assert.HasCount(0, random.Bounds);
        Assert.AreEqual(0, battle.CurrentActor!.RoleId);
    }

    private static ItemDefinition Item(int id, int type, params (int Word, int Value)[] changes)
    {
        var words = new int[ItemWords.Count]; words[0] = id; words[ItemWords.ItemType] = type;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, $"Item {id}", words);
    }
    private static RoleDefinition Role(int id, params (int Word, int Value)[] changes)
    {
        var words = new int[RoleWords.Count]; words[0] = id; words[RoleWords.Hp] = words[RoleWords.MaximumHp] = 100;
        words[RoleWords.Mp] = words[RoleWords.MaximumMp] = 100; words[RoleWords.PhysicalPower] = 100; words[RoleWords.Speed] = id == 0 ? 60 : 30;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        for (var slot = 0; slot < 4; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, $"Role {id}", "", words);
    }
    private static PartyState Party(params RoleDefinition[] roles) => new(new PartyDefinition(roles, [0, -1, -1, -1, -1, -1]));
    private static BattleSession Ready(PartyState party, ItemDefinition[] items, Inventory inventory, NativeRandom random, int enemyX = 12)
    {
        var words = new int[93]; for (var index = 9; index < 21; index++) words[index] = -1;
        for (var index = 33; index < 53; index++) words[index] = -1;
        words[15] = 0; words[21] = 10; words[27] = 10; words[33] = 1; words[53] = enemyX; words[73] = 10;
        return new(new(0, words), new(0, new short[8192]), party, new Dictionary<int, MagicDefinition>(),
            items.ToDictionary(item => item.Id), random: random.Next, inventory: inventory);
    }
    private sealed class Inventory(params (int Id, int Count)[] items) : IBattleInventory
    {
        private readonly Dictionary<int, int> _items = items.ToDictionary(pair => pair.Id, pair => pair.Count);
        public IReadOnlyDictionary<int, int> Items => _items;
        public int Consumes { get; private set; }
        public bool TryConsume(int itemId, int count)
        {
            if (count <= 0 || _items.GetValueOrDefault(itemId) < count) return false;
            _items[itemId] -= count; Consumes++; return true;
        }
    }
    private sealed class NativeRandom(uint seed = 1)
    {
        public uint State { get; private set; } = seed;
        public List<int> Bounds { get; } = [];
        public int Next(int bound)
        {
            if (bound <= 1 || bound > 30000) return 0;
            Bounds.Add(bound); State = unchecked(State * 0x41C64E6D + 0x3039);
            return (int)((State >> 16) & 0x7fff) % bound;
        }
    }
}
