using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed record BattleRoleSettlement(int RoleId, int Side, int ExperienceGained, int OldLevel, int NewLevel,
    bool ExperienceMessageRequired, bool Practiced, int PracticeItemId, IReadOnlyList<int> IncreasedMagicSlots,
    int? CraftedItemId, int CraftedCount);
public sealed record BattleSettlementResult(int SharedExperience, IReadOnlyList<BattleRoleSettlement> Roles, IReadOnlyList<string> Notices);

/// <summary>
/// DOS baseline settlement, deliberately independent of BattleSession presentation.
/// References: OpenLegend research/evidence/functions/Z_DAT/{0x3B387,0x3B6BE,0x3BA85,0x3C2AC}.md
/// sections describing the original machine, not the current NG+ C++ replacements.
/// Caller must settle a battle once. Role/inventory changes commit after calculation;
/// the shared random callback is consumed in order and cannot be rewound on failure.
/// Inventory normalization: positive-count dictionary + distinct ID set, not the DOS
/// 200-slot buffer. Duplicate IDs, negative counts and stale empty-slot counts are
/// not reconstructed. Crafting keeps existing inventory semantics and checked int32.
/// Messages describe completed results; DOS per-message delayed commit is not emulated.
/// </summary>
public static class BattleSettlement
{
    public static BattleSettlementResult Settle(BattleDefinition battle, IReadOnlyList<BattleCombatant> combatants,
        bool victory, int getExp, ProgressionDefinition rules, IReadOnlyDictionary<int, ItemDefinition> items,
        Func<int, int> random, IDictionary<int, int> inventory, ISet<int> itemIds)
    {
        ArgumentNullException.ThrowIfNull(random);
        var roles = combatants.Select(actor => actor.Role).Distinct().ToDictionary(role => role,
            role => Enumerable.Range(0, RoleWords.Count).Select(word => role[word]).ToArray());
        var bag = inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var ids = itemIds.ToHashSet();
        foreach (var actor in combatants.Where(actor => actor.Side == 0 && (getExp == 1 || victory)))
        {
            var words = roles[actor.Role];
            var practice = S(words[RoleWords.PracticeItem]);
            if (practice != -1 && !items.ContainsKey(practice)) throw new ArgumentException($"Unknown practice item {practice}.");
        }
        var rewards = combatants.Select(actor => S(actor.RewardExperience)).ToArray();
        var living = 0;
        foreach (var actor in combatants)
        {
            var words = roles[actor.Role];
            if (actor.Side == 1)
            {
                words[RoleWords.Hp] = S(words[RoleWords.MaximumHp]); words[RoleWords.Mp] = S(words[RoleWords.MaximumMp]);
                words[RoleWords.PhysicalPower] = 100; words[RoleWords.Hurt] = words[RoleWords.Poison] = 0;
            }
            else if (S(words[RoleWords.Hp]) > 0) living++;
        }
        var shared = victory ? battle.Experience / Math.Max(1, living) : 0;
        for (var index = 0; index < combatants.Count; index++)
        {
            var actor = combatants[index]; var words = roles[actor.Role];
            if (actor.Side != 0) continue;
            var alive = S(words[RoleWords.Hp]) > 0;
            if (victory && alive) rewards[index] = S(rewards[index] + shared);
            var floor = S(words[RoleWords.MaximumHp]) / 5;
            if (!alive || S(words[RoleWords.Hp]) < floor) words[RoleWords.Hp] = floor;
            if (!alive && S(words[RoleWords.PhysicalPower]) < 10) words[RoleWords.PhysicalPower] = 10;
        }
        var notices = new List<string>();
        var results = new List<BattleRoleSettlement>();
        for (var index = 0; index < combatants.Count; index++)
        {
            var actor = combatants[index]; var words = roles[actor.Role];
            var reward = rewards[index];
            // Original sign-extension, low32 shift and unsigned division. This is
            // not ordinary signed "80 percent" for negative legacy reward words.
            var training = unchecked((uint)(reward * rules.RewardNumerator)) / (uint)rules.RewardDenominator;
            words[RoleWords.Experience] = AccumulateExperience(words[RoleWords.Experience], reward);
            words[RoleWords.ItemExperience] = AccumulateExperience(words[RoleWords.ItemExperience], training);
            words[RoleWords.MakeItemExperience] = AccumulateExperience(words[RoleWords.MakeItemExperience], training);
            var message = actor.Side == 0 && (getExp == 1 || victory);
            var oldLevel = S(words[RoleWords.Level]);
            var practiced = false; var practiceItem = S(words[RoleWords.PracticeItem]);
            var improved = new List<int>(); int? product = null; var produced = 0;
            if (message)
            {
                notices.Add($"{actor.Role.Name} 获得经验 {reward}");
                if (LevelUp(words, rules, random)) notices.Add($"{actor.Role.Name} 升至 {words[RoleWords.Level]} 级");
                if (practiceItem != -1)
                {
                    var item = items[practiceItem];
                    practiced = Practice(words, item, rules, improved);
                    if (practiced) notices.Add($"{actor.Role.Name} 修炼 {item.Name} 成功");
                    var crafted = Craft(words, item, rules, items, random, bag, ids);
                    product = crafted.Item; produced = crafted.Count;
                    if (product is { } id) notices.Add($"{actor.Role.Name} 制造 {items[id].Name} × {produced}");
                }
            }
            results.Add(new(actor.Role.Id, actor.Side, reward, oldLevel, S(words[RoleWords.Level]), message,
                practiced, practiceItem, improved.AsReadOnly(), product, produced));
        }
        foreach (var (role, words) in roles)
            for (var word = 0; word < words.Length; word++) role.Set(word, words[word]);
        for (var index = 0; index < combatants.Count; index++) combatants[index].RewardExperience = rewards[index];
        inventory.Clear(); foreach (var pair in bag) inventory.Add(pair.Key, pair.Value);
        itemIds.Clear(); foreach (var id in ids) itemIds.Add(id);
        return new(shared, results.AsReadOnly(), notices.AsReadOnly());
    }

    private static int S(int value) => unchecked((short)value);
    private static int U(int value) => unchecked((ushort)value);
    private static int AddWord(int value, int delta) => S(unchecked(value + delta));
    private static int AccumulateExperience(int value, long delta) => Math.Min(60000, (int)unchecked((ushort)(value + delta)));
    private static int Draw(Func<int, int> random, int bound)
    {
        var value = random(bound);
        return value >= 0 && value < bound ? value : throw new InvalidOperationException("Battle random callback returned an out-of-range value.");
    }

    private static bool LevelUp(int[] words, ProgressionDefinition rules, Func<int, int> random)
    {
        var oldLevel = S(words[RoleWords.Level]); var experience = U(words[RoleWords.Experience]);
        if (oldLevel < 0 || oldLevel >= 30 || experience < rules.ExperienceThresholds[oldLevel]) return false;
        var next = oldLevel;
        for (var level = oldLevel; level < 30; level++) if (experience >= rules.ExperienceThresholds[level]) next = level + 1;
        var gained = next - oldLevel;
        if (gained <= 0) return false;
        var iq = S(words[RoleWords.Iq]);
        var growth = Draw(random, iq < 30 ? 2 : iq < 50 ? 3 : iq < 70 ? 4 : iq < 90 ? 5 : 6) + 1;
        var hpGain = unchecked((U(words[RoleWords.IncreasedLife]) + Draw(random, 3)) * 3 * gained);
        words[RoleWords.MaximumHp] = Math.Min(rules.MaximumHp, AddWord(words[RoleWords.MaximumHp], hpGain));
        words[RoleWords.MaximumMp] = Math.Min(rules.MaximumMp, AddWord(words[RoleWords.MaximumMp], (9 - growth) * 4 * gained));
        foreach (var word in new[] { RoleWords.Attack, RoleWords.Speed, RoleWords.Defence }) words[word] = AddWord(words[word], growth * gained);
        foreach (var word in new[] { RoleWords.Medicine, RoleWords.UsePoison, RoleWords.Detoxification, RoleWords.Fist, RoleWords.Sword, RoleWords.Knife })
            if (S(words[word]) > 20) words[word] = AddWord(words[word], Draw(random, 3));
        words[RoleWords.HiddenWeapon] = AddWord(words[RoleWords.HiddenWeapon], Draw(random, 3));
        foreach (var word in new[] { RoleWords.Attack, RoleWords.Speed, RoleWords.Defence, RoleWords.Medicine, RoleWords.UsePoison,
            RoleWords.Detoxification, RoleWords.HiddenWeapon, RoleWords.Fist, RoleWords.Sword, RoleWords.Knife }) words[word] = Math.Min(100, S(words[word]));
        words[RoleWords.Level] = next; words[RoleWords.Hp] = words[RoleWords.MaximumHp]; words[RoleWords.Mp] = words[RoleWords.MaximumMp];
        words[RoleWords.Hurt] = words[RoleWords.Poison] = 0; words[RoleWords.PhysicalPower] = 100;
        return true;
    }

    private static bool Practice(int[] words, ItemDefinition item, ProgressionDefinition rules, List<int> improved)
    {
        var magic = item[ItemWords.MagicId]; var firstLevel = 0;
        if (magic != -1)
            for (var slot = 0; slot < RoleWords.MagicCount; slot++)
                if (S(words[RoleWords.MagicIdBegin + slot]) == magic) { firstLevel = U(words[RoleWords.MagicLevelBegin + slot]) / 100; break; }
        if (firstLevel >= 9) return false;
        var factor = rules.AptitudeBase - S(words[RoleWords.Iq]) / rules.AptitudeStep;
        var tier = magic == -1 ? rules.UnassociatedFirstLevel : firstLevel + rules.UnlearnedLevel;
        var cost = unchecked(unchecked(item[ItemWords.NeedExperience] * factor) * tier);
        if (U(words[RoleWords.ItemExperience]) < cost) return false;
        words[RoleWords.MaximumHp] = Math.Min(rules.MaximumHp, AddWord(words[RoleWords.MaximumHp], item[ItemWords.AddMaximumHp]));
        words[RoleWords.MaximumMp] = Math.Min(rules.MaximumMp, AddWord(words[RoleWords.MaximumMp], item[ItemWords.AddMaximumMp]));
        if (item[ItemWords.ChangeMpType] == 2) words[RoleWords.MpType] = 2;
        for (var offset = 0; offset < 14; offset++) words[RoleWords.Attack + offset] = Math.Clamp(AddWord(words[RoleWords.Attack + offset], item[ItemWords.AddAttack + offset]), 0, 100);
        if (S(words[RoleWords.AttackTwice]) == 0) words[RoleWords.AttackTwice] = item[ItemWords.AddAttackTwice];
        words[RoleWords.AttackWithPoison] = Math.Clamp(AddWord(words[RoleWords.AttackWithPoison], item[ItemWords.AddAttackWithPoison]), 0, 100);
        words[RoleWords.ItemExperience] = 0;
        if (magic <= 0) return true;
        var found = false;
        for (var slot = 0; slot < RoleWords.MagicCount; slot++)
        {
            if (S(words[RoleWords.MagicIdBegin + slot]) != magic) continue;
            found = true;
            if (U(words[RoleWords.MagicLevelBegin + slot]) >= 899) continue;
            words[RoleWords.MagicLevelBegin + slot] = U(AddWord(words[RoleWords.MagicLevelBegin + slot], 100)); improved.Add(slot);
        }
        if (!found)
            for (var slot = 0; slot < RoleWords.MagicCount; slot++)
                if (S(words[RoleWords.MagicIdBegin + slot]) <= 0) { words[RoleWords.MagicIdBegin + slot] = magic; break; }
        return true;
    }

    private static (int? Item, int Count) Craft(int[] words, ItemDefinition manual, ProgressionDefinition rules,
        IReadOnlyDictionary<int, ItemDefinition> items, Func<int, int> random, Dictionary<int, int> bag, HashSet<int> ids)
    {
        var need = manual[ItemWords.NeedMakeItemExperience];
        var cost = unchecked(need * (rules.AptitudeBase - S(words[RoleWords.Iq]) / rules.AptitudeStep));
        if (need <= 0 || cost <= 0 || U(words[RoleWords.MakeItemExperience]) < cost) return (null, 0);
        var material = manual[ItemWords.NeedMaterial];
        if (!ids.Contains(material)) return (null, 0);
        var eligible = Enumerable.Range(0, 5).Where(slot => manual[ItemWords.MakeItemBegin + slot] != -1 &&
            bag.GetValueOrDefault(material) >= manual[ItemWords.MakeItemCountBegin + slot]).ToHashSet();
        if (eligible.Count == 0) return (null, 0);
        foreach (var slot in eligible)
            if (!items.ContainsKey(manual[ItemWords.MakeItemBegin + slot])) throw new ArgumentException("Crafting references an unknown product.");
        int recipe; var tries = 0;
        do
        {
            if (++tries > 10000) throw new InvalidOperationException("Crafting random callback failed to select an eligible recipe.");
            recipe = Draw(random, 5);
        } while (!eligible.Contains(recipe));
        var product = manual[ItemWords.MakeItemBegin + recipe];
        var existing = ids.Contains(product);
        if (!existing && ids.Count >= 200) return (null, 0);
        var count = existing ? Draw(random, 3) + 1 : 1;
        var total = checked(bag.GetValueOrDefault(product) + count);
        bag[product] = total; ids.Add(product);
        var remaining = checked(bag.GetValueOrDefault(material) - manual[ItemWords.MakeItemCountBegin + recipe]);
        if (remaining <= 0) { bag.Remove(material); ids.Remove(material); } else bag[material] = remaining;
        words[RoleWords.MakeItemExperience] = 0;
        return (product, count);
    }
}
