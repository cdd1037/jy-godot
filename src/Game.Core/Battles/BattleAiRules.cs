using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public enum BattleAiAction { None, Move, Attack, UsePoison, Detox, Medicine, Item, Wait, RequestMedicine, RequestDetox, ThrowingWeapon, Escape }
public sealed record BattleAiPrelude(int AlliedTotal, int AlliedCount, int OpponentTotal, int OpponentCount);
public sealed record BattleAiDecision(BattleAiAction Action, int TargetSlot = -1, int ItemSlot = -1, int ItemId = -1);

/// <summary>
/// Historical DOS 33599 / 33C4D / 33E93 / 340D9 / 341F6 / 343DA / 34550.
/// Does not use the reference project's current NG+ hurt ratios or widened totals.
/// Carried source order is authoritative; quantities are deliberately not selection gates.
/// </summary>
public static partial class BattleAiRules
{
    private static int W(int value) => BattleAttackRules.Word(value);
    public static BattleAiPrelude Prelude(IReadOnlyList<BattleCombatant> combatants, BattleCombatant actor)
    {
        var allied = 0; var opponents = 0; var allies = 0; var enemies = 0;
        foreach (var person in combatants)
        {
            if (person.Side == actor.Side) { allied = W(W(allied + person.Role[RoleWords.Attack]) + person.Hp); allies++; }
            else { opponents = W(W(opponents + person.Role[RoleWords.Attack]) + person.Hp); enemies++; }
        }
        return new(allied, allies, opponents, enemies);
    }
    public static BattleAiDecision Choose(IReadOnlyList<BattleCombatant> combatants, BattleCombatant actor,
        IReadOnlyDictionary<int, MagicDefinition> magics, IReadOnlyDictionary<int, ItemDefinition> items,
        BattleAiPrelude prelude, Func<int, int> random, Func<BattleCombatant, BattleAiAction>? previousAction = null)
    {
        if (actor.IsFriendly) throw new ArgumentException("Enemy AI cannot select from the player's backpack.");
        var role = actor.Role; int R(int bound) => bound <= 1 ? 0 : random(bound);
        var choice = new BattleAiDecision(role[RoleWords.PhysicalPower] < 10 ? BattleAiAction.Wait : BattleAiAction.None);
        bool Probability(int value, int maximum, params int[] thresholds)
        {
            for (var i = 0; i < thresholds.Length; i++) if (value < maximum / (i + 2) && R(10) < thresholds[i]) return true;
            return false;
        }
        var lowHp = role[RoleWords.Hp] < 20 || role[RoleWords.Hurt] > 50 ||
            Probability(role[RoleWords.Hp], role[RoleWords.MaximumHp], 3, 5, 7, 9);
        if (lowHp) choice = LowHp(combatants, actor, items); // Native low-HP failure can overwrite initial Wait.
        if (choice.Action == BattleAiAction.None && R(10) < role[RoleWords.Poison] / 10) choice = Poisoned(combatants, actor, items);
        if (choice.Action == BattleAiAction.None && Probability(role[RoleWords.Mp], role[RoleWords.MaximumMp], 2, 4, 6, 8))
            choice = Carried(actor, items, item => item[ItemWords.AddMp] > 0);
        if (choice.Action == BattleAiAction.None && role[RoleWords.PhysicalPower] > 50 && AbilityGate(role[RoleWords.Medicine], R))
            choice = MedicineTarget(combatants, actor, R, previousAction);
        if (choice.Action == BattleAiAction.None && role[RoleWords.PhysicalPower] > 50 && AbilityGate(role[RoleWords.Detoxification], R))
            choice = DetoxTarget(combatants, actor, R, previousAction);
        if (choice.Action == BattleAiAction.None && R(10) < 5)
        {
            var escape = role[RoleWords.Hp] < 20 ||
                role[RoleWords.Hp] < role[RoleWords.MaximumHp] / 4 && R(10) < 6 ||
                role[RoleWords.Hp] < role[RoleWords.MaximumHp] / 5 && R(10) < 8;
            if (escape) choice = new(BattleAiAction.Escape);
        }
        return choice.Action == BattleAiAction.None ? Offensive(combatants, actor, magics, items, prelude, R) : choice;
    }
    private static bool AbilityGate(int ability, Func<int, int> random) =>
        ability >= 20 && random(10) < 4 || ability >= 40 && random(10) < 6 || ability >= 60 && random(10) < 8 || ability >= 80;
    private static IEnumerable<BattleCombatant> Allies(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor) =>
        persons.Where(person => !ReferenceEquals(person, actor) && person.Side == actor.Side && !person.Hidden);
    private static IEnumerable<(int Slot, ItemDefinition Item)> Sources(BattleCombatant actor, IReadOnlyDictionary<int, ItemDefinition> items)
    {
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++)
        {
            var id = actor.Role[RoleWords.TakingItemBegin + slot]; if (id < 0) continue;
            if (!items.TryGetValue(id, out var item)) throw new ArgumentException($"Unknown carried AI item {id}.");
            yield return (slot, item);
        }
    }
    private static BattleAiDecision Carried(BattleCombatant actor, IReadOnlyDictionary<int, ItemDefinition> items, Func<ItemDefinition, bool> match)
    {
        foreach (var source in Sources(actor, items)) if (match(source.Item)) return new(BattleAiAction.Item, actor.Slot, source.Slot, source.Item.Id);
        return new(BattleAiAction.None);
    }
    public static BattleAiDecision LowHp(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor, IReadOnlyDictionary<int, ItemDefinition> items)
    {
        var role = actor.Role;
        if (role[RoleWords.Medicine] >= 20 && role[RoleWords.PhysicalPower] >= 50 && role[RoleWords.Medicine] > (long)role[RoleWords.Hurt] - 30)
            return new(BattleAiAction.Medicine, actor.Slot);
        var item = Carried(actor, items, item => item[ItemWords.AddHp] > 0); if (item.Action != BattleAiAction.None) return item;
        foreach (var other in Allies(persons, actor))
            if (other.Role[RoleWords.Medicine] > 20 && other.Role[RoleWords.Medicine] > (long)role[RoleWords.Hurt] - 30)
                return new(BattleAiAction.RequestMedicine, other.Slot);
        return new(BattleAiAction.None);
    }
    public static BattleAiDecision Poisoned(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor, IReadOnlyDictionary<int, ItemDefinition> items)
    {
        var role = actor.Role;
        if (role[RoleWords.Detoxification] > 20 && role[RoleWords.Detoxification] > (long)role[RoleWords.Poison] - 30 && role[RoleWords.PhysicalPower] > 50)
            return new(BattleAiAction.Detox, actor.Slot);
        var item = Carried(actor, items, item => item[ItemWords.AddPoison] < 0); if (item.Action != BattleAiAction.None) return item;
        foreach (var other in Allies(persons, actor))
            if (other.Role[RoleWords.Detoxification] > 20 && other.Role[RoleWords.Detoxification] > (long)role[RoleWords.Poison] - 30)
                return new(BattleAiAction.RequestDetox, other.Slot);
        return new(BattleAiAction.None);
    }
    public static BattleAiDecision MedicineTarget(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor, Func<int, int> random,
        Func<BattleCombatant, BattleAiAction>? previousAction = null)
    {
        foreach (var other in Allies(persons, actor))
        {
            var role = other.Role; if (actor.Role[RoleWords.Medicine] <= (long)role[RoleWords.Hurt] - 30) continue;
            var select = previousAction?.Invoke(other) == BattleAiAction.RequestMedicine || other.Hp < 20 || role[RoleWords.Hurt] > 40;
            if (!select && other.Hp < other.MaximumHp / 2) select = random(10) < 7;
            if (!select && other.Hp < other.MaximumHp / 3) select = random(10) < 8;
            if (!select && other.Hp < other.MaximumHp / 4) select = random(10) < 9;
            if (!select && other.Hp < other.MaximumHp / 5) select = true;
            if (select) return new(BattleAiAction.Medicine, other.Slot);
        }
        return new(BattleAiAction.None);
    }
    public static BattleAiDecision DetoxTarget(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor, Func<int, int> random,
        Func<BattleCombatant, BattleAiAction>? previousAction = null)
    {
        foreach (var other in Allies(persons, actor))
        {
            var poison = other.Role[RoleWords.Poison]; if (actor.Role[RoleWords.Detoxification] <= (long)poison - 30) continue;
            var select = previousAction?.Invoke(other) == BattleAiAction.RequestDetox;
            if (!select && poison > 10) select = random(10) < 4;
            if (!select && poison > 20) select = random(10) < 6;
            if (!select && poison > 30) select = random(10) < 8;
            if (!select && poison > 40) select = true;
            if (select) return new(BattleAiAction.Detox, other.Slot);
        }
        return new(BattleAiAction.None);
    }
    public static BattleAiDecision Offensive(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor,
        IReadOnlyDictionary<int, MagicDefinition> magics, IReadOnlyDictionary<int, ItemDefinition> items,
        BattleAiPrelude prelude, Func<int, int> random)
    {
        if (prelude.AlliedCount <= 0 || prelude.OpponentCount <= 0) throw new ArgumentException("AI prelude requires both sides.");
        var role = actor.Role;
        if (prelude.OpponentTotal / prelude.OpponentCount / 2 > (long)actor.Hp + role[RoleWords.Attack] && prelude.AlliedTotal > 2L * prelude.OpponentTotal)
        {
            if (role[RoleWords.Medicine] >= 20 && actor.PhysicalPower >= 50)
            {
                var best = 0; BattleCombatant? selected = null;
                foreach (var other in Allies(persons, actor)) if (other.Hp < other.MaximumHp && (long)other.MaximumHp - other.Hp > best)
                { best = W(other.MaximumHp - other.Hp); selected = other; }
                if (selected is not null) return new(BattleAiAction.Medicine, selected.Slot);
            }
            else if (role[RoleWords.Detoxification] >= 20 && actor.PhysicalPower >= 50)
            {
                var best = 0; BattleCombatant? selected = null;
                foreach (var other in Allies(persons, actor)) if (other.Role[RoleWords.Poison] > best)
                { best = other.Role[RoleWords.Poison]; selected = other; }
                if (selected is not null) return new(BattleAiAction.Detox, selected.Slot);
            }
        }
        if ((long)role[RoleWords.UsePoison] - role[RoleWords.Attack] > random(50) && random(150) < role[RoleWords.UsePoison]) return new(BattleAiAction.UsePoison);
        foreach (var source in Sources(actor, items))
        {
            var item = source.Item;
            if (item[ItemWords.AddHp] < 0 && Math.Abs((long)item[ItemWords.AddHp]) > role[RoleWords.Attack] && random(10) < 6 ||
                item[ItemWords.AddPoison] > 0 && item[ItemWords.AddPoison] > role[RoleWords.Attack] && random(10) < 3)
                return new(BattleAiAction.ThrowingWeapon, -1, source.Slot, item.Id);
        }
        if (actor.PhysicalPower <= 10) return new(BattleAiAction.None);
        var minimumMp = 1000;
        for (var slot = 0; slot < RoleWords.MagicCount; slot++)
        {
            var id = role[RoleWords.MagicIdBegin + slot]; if (id is -1 or 0) continue;
            if (!magics.TryGetValue(id, out var magic)) throw new ArgumentException($"Unknown AI magic {id}.");
            minimumMp = Math.Min(minimumMp, magic[MagicWords.NeedMp]);
        }
        return new(actor.Mp >= minimumMp ? BattleAiAction.Attack : BattleAiAction.None);
    }
}
