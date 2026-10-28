using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed record BattleSupportResult(BattleSupportAction Action, int ActorSlot, int? TargetSlot,
    GridPosition Position, int Amount);

public sealed partial class BattleSession
{
    public BattleSupportResult? LastSupportResult { get; private set; }
    public bool CanUseSupport(BattleSupportAction action) => CurrentActor is { } actor && BattleSupportRules.CanUse(actor.Role, action);
    public int SupportRange(BattleSupportAction action) => CurrentActor is { } actor ? BattleSupportRules.Range(actor.Role, action) : 0;

    /// <summary>
    /// Targeting mode 1 checks the upper-layer path, not occupancy or side. The original
    /// permits confirming an empty/wrong-side cell and pays the action's common finish cost.
    /// </summary>
    public bool CanTargetSupport(BattleSupportAction action, GridPosition target)
    {
        if (!BattlefieldDefinition.Contains(target) || !CanUseSupport(action)) return false;
        var actor = CurrentActor!; var range = SupportRange(action);
        var distances = BattlePathing.Distances(Battlefield, Combatants, actor.Position, targeting: true, maximum: range);
        return distances.TryGetValue(target, out var distance) && distance <= range;
    }
    public bool UsePoison(GridPosition target) => ApplySupport(BattleSupportAction.UsePoison, target);
    public bool Detox(GridPosition target) => ApplySupport(BattleSupportAction.Detox, target);
    public bool Medicine(GridPosition target) => ApplySupport(BattleSupportAction.Medicine, target);

    private bool ApplySupport(BattleSupportAction action, GridPosition position)
    {
        if (!CanTargetSupport(action, position)) return Fail("Support action is unavailable or the target is outside its targeting path range.");
        var actor = CurrentActor!; var patient = OccupantAt(position); var amount = 0; int? hitSlot = null;
        try
        {
            var matchingSide = patient is not null && (action == BattleSupportAction.UsePoison
                ? patient.IsFriendly != actor.IsFriendly : patient.IsFriendly == actor.IsFriendly);
            if (matchingSide)
            {
                hitSlot = patient!.Slot;
                if (action == BattleSupportAction.Medicine)
                {
                    var result = BattleSupportRules.Medicine(actor.Role, patient.Role, _random);
                    patient.Role.Set(RoleWords.Hp, result.Hp); patient.Role.Set(RoleWords.Hurt, result.Hurt);
                    actor.Role.Set(RoleWords.PhysicalPower, result.PhysicalPower); amount = result.Amount;
                }
                else
                {
                    var result = action == BattleSupportAction.UsePoison ? BattleSupportRules.UsePoison(actor.Role, patient.Role) :
                        BattleSupportRules.Detox(actor.Role, patient.Role, _random);
                    patient.Role.Set(RoleWords.Poison, result.Poison); amount = result.Amount;
                }
            }
            if (position != actor.Position) actor.Facing = BattleMagicRules.Facing(actor.Position, position);
            LastDamage = []; // Support amounts are poison/healing, never HP attack damage.
            LastSupportResult = new(action, actor.Slot, hitSlot, position, BattleAttackRules.Word(amount));
            actor.RewardExperience = BattleAttackRules.Word(actor.RewardExperience + 1);
            actor.Role.Set(RoleWords.PhysicalPower, BattleSupportRules.FinishPhysicalPower(actor.PhysicalPower));
            EndAction(); LastError = null; return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return Fail(error.Message); }
    }
}
