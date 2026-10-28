using JinYong.Core.Items;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

/// <summary>The actual shared party backpack, never a copied battle inventory.</summary>
public interface IBattleInventory
{
    IReadOnlyDictionary<int, int> Items { get; }
    bool TryConsume(int itemId, int count);
}

public sealed partial class BattleSession
{
    public ItemUseResult? LastItemUse { get; private set; }
    public int? LastThrownEffectId { get; private set; }

    public IReadOnlyList<int> AvailableBattleItems()
    {
        if (IsPresenting || CurrentActor is not { } actor) return [];
        IEnumerable<int> ids = actor.IsFriendly ? _battleInventory?.Items.Where(pair => pair.Value > 0).Select(pair => pair.Key) ?? [] :
            Enumerable.Range(0, RoleWords.TakingItemCount).Where(slot => actor.Role[RoleWords.TakingCountBegin + slot] > 0)
                .Select(slot => actor.Role[RoleWords.TakingItemBegin + slot]);
        return ids.Where(id => _items.TryGetValue(id, out var item) && item[ItemWords.ItemType] is 3 or 4).Distinct().ToArray();
    }
    private bool HasCarriedItem(BattleCombatant actor, int itemId) => !actor.IsFriendly &&
        Enumerable.Range(0, RoleWords.TakingItemCount).Any(slot => actor.Role[RoleWords.TakingItemBegin + slot] == itemId);

    private bool ConsumeBattleItem(BattleCombatant actor, int itemId, bool automatic = false)
    {
        if (actor.IsFriendly) return _battleInventory?.TryConsume(itemId, 1) == true;
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++)
        {
            if (actor.Role[RoleWords.TakingItemBegin + slot] != itemId || !automatic && actor.Role[RoleWords.TakingCountBegin + slot] <= 0) continue;
            var remaining = BattleAttackRules.Word(actor.Role[RoleWords.TakingCountBegin + slot] - 1);
            if (remaining > 0) actor.Role.Set(RoleWords.TakingCountBegin + slot, remaining);
            else
            {
                for (var next = slot + 1; next < RoleWords.TakingItemCount; next++)
                {
                    actor.Role.Set(RoleWords.TakingItemBegin + next - 1, actor.Role[RoleWords.TakingItemBegin + next]);
                    actor.Role.Set(RoleWords.TakingCountBegin + next - 1, actor.Role[RoleWords.TakingCountBegin + next]);
                }
                actor.Role.Set(RoleWords.TakingItemBegin + RoleWords.TakingItemCount - 1, -1);
                actor.Role.Set(RoleWords.TakingCountBegin + RoleWords.TakingItemCount - 1, 0);
            }
            return true;
        }
        return false;
    }
    public bool UseBattleItem(int itemId) => ApplyBattleItem(itemId, automatic: false);
    internal bool UseAiBattleItem(int itemId) => ApplyBattleItem(itemId, automatic: true);

    private bool ApplyBattleItem(int itemId, bool automatic)
    {
        if (IsPresenting || CurrentActor is not { } actor || !_items.TryGetValue(itemId, out var item) || item[ItemWords.ItemType] != 3 ||
            (automatic ? !HasCarriedItem(actor, itemId) : !AvailableBattleItems().Contains(itemId)))
            return Fail("This medicine is not available to the active fighter.");
        try
        {
            var before = CaptureCombatantSnapshots(); var actorSlot = actor.Slot;
            var result = ItemUseRules.Calculate(actor.Role, actor.Role, _items[itemId], _random);
            // Manual no-effect medicine still ends the action without consuming;
            // DOS enemy AI always consumes its carried source, including no-effect.
            if ((result.HasEffect || !actor.IsFriendly) && !ConsumeBattleItem(actor, itemId, automatic)) return Fail("Medicine source is no longer available.");
            ItemUseRules.Apply(actor.Role, result);
            LastItemUse = result; LastThrownEffectId = null; LastDamage = [];
            QueueItemPresentation(before, actorSlot, result); EndAction(); LastError = null; return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return Fail(error.Message); }
    }
    public bool CanTargetThrownItem(int itemId, GridPosition target)
        => TargetThrownItemInRange(itemId, target, automatic: false);

    private bool TargetThrownItemInRange(int itemId, GridPosition target, bool automatic)
    {
        if (IsPresenting || CurrentActor is not { } actor || !BattlefieldDefinition.Contains(target) ||
            !_items.TryGetValue(itemId, out var item) || item[ItemWords.ItemType] != 4 ||
            (automatic ? !HasCarriedItem(actor, itemId) : !AvailableBattleItems().Contains(itemId)) ||
            OccupantAt(target) is not { IsAlive: true } victim || victim.IsFriendly == actor.IsFriendly) return false;
        var range = actor.Role[RoleWords.HiddenWeapon] / 15 + 1;
        if (range < 0) return false;
        var distances = BattlePathing.Distances(Battlefield, Combatants, actor.Position, targeting: true, maximum: range);
        return distances.TryGetValue(target, out var distance) && distance <= range;
    }
    /// <summary>
    /// Historical DOS sub_3A30B (manual) and sub_3598C (enemy AI), not current
    /// OpenLegend NG+ throwing_skill amplification/poison overflow. Numeric state
    /// commits once; the optional presentation queue retains before/after views.
    /// </summary>
    public bool ThrowItem(int itemId, GridPosition target) => ApplyThrownItem(itemId, target, automatic: false);
    internal bool ThrowAiItem(int itemId, GridPosition target) => ApplyThrownItem(itemId, target, automatic: true);

    private bool ApplyThrownItem(int itemId, GridPosition target, bool automatic)
    {
        if (!TargetThrownItemInRange(itemId, target, automatic) || CurrentActor is not { } actor) return Fail("Thrown-item target or inventory is unavailable.");
        var victim = OccupantAt(target)!; var role = victim.Role; var item = _items[itemId];
        try { ValidateThrownItemPresentation(item); }
        catch (ArgumentException error) { return Fail(error.Message); }
        var before = CaptureCombatantSnapshots(); var actorSlot = actor.Slot;
        // Negative hurt follows an undefined stale-register branch in DOS;
        // negative maximum HP is outside the supported clamping domain. The
        // historical machine audit explicitly treats these as safety adaptation.
        if (role[RoleWords.Hurt] < 0 || role[RoleWords.MaximumHp] < 0) return Fail("Thrown-item target has invalid HP or hurt.");
        // Calculate before consuming: a broken host RNG must never drop an item.
        // With the authoritative single-threaded inventory, the prior availability
        // check guarantees consumption; adapters must keep TryConsume atomic.
        var hurt = role[RoleWords.Hurt]; var divisor = hurt == 0 ? 4 : hurt <= 33 ? 3 : hurt <= 66 ? 2 : 1;
        var basis = BattleAttackRules.Word(item[ItemWords.AddHp] / divisor - _random(5));
        var hpDelta = BattleAttackRules.Word((basis - 2 * actor.Role[RoleWords.HiddenWeapon]) / 3);
        var nextHurt = Math.Clamp(BattleAttackRules.Word(hurt - hpDelta / 4), 0, 99);
        var nextHp = Math.Clamp(BattleAttackRules.Word(role[RoleWords.Hp] + hpDelta), 0, role[RoleWords.MaximumHp]);
        var poison = item[ItemWords.AddPoison]; int poisonDelta;
        if (actor.IsFriendly)
        {
            if (poison > 0)
            {
                poisonDelta = BattleAttackRules.Word((poison - actor.Role[RoleWords.HiddenWeapon]) / 2 - role[RoleWords.AntiPoison]);
                if (role[RoleWords.AntiPoison] >= 100 || poisonDelta < 0) poisonDelta = 0;
                poisonDelta /= 2;
            }
            else poisonDelta = poison / 2 + _random(5) - _random(5);
        }
        else poisonDelta = poison >= 0 ? poison : (poison - actor.Role[RoleWords.HiddenWeapon]) / 2;
        var nextPoison = Math.Clamp(BattleAttackRules.Word(role[RoleWords.Poison] + poisonDelta), 0, 99);
        var damage = BattleAttackRules.Word(Math.Abs(BattleAttackRules.Word(nextHp - role[RoleWords.Hp])));
        if (!ConsumeBattleItem(actor, itemId, automatic)) return Fail("Thrown-item source is no longer available.");
        if (actor.IsFriendly) actor.Facing = BattleMagicRules.Facing(actor.Position, target);
        role.Set(RoleWords.Hp, nextHp); role.Set(RoleWords.Hurt, nextHurt); role.Set(RoleWords.Poison, nextPoison);
        LastItemUse = null; LastThrownEffectId = item[ItemWords.HiddenWeaponEffectId];
        LastDamage = [new(actor.Slot, victim.Slot, target, damage, false)];
        QueueThrownItemPresentation(before, actorSlot, itemId, target); EndAction(); LastError = null; return true;
    }
}
