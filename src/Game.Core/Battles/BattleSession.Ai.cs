using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed partial class BattleSession
{
    private enum AiStage { Dispatch, Attack, AttackMoved, PoisonMoved, SupportMoved, ThrowMoved, ItemMoved, RequestMoved, EscapeMoved, MoveFinished }
    private sealed class AiPlan(BattleCombatant actor, BattleAiPrelude prelude, BattleAiDecision decision)
    {
        public BattleCombatant Actor { get; } = actor;
        public BattleAiPrelude Prelude { get; } = prelude;
        public BattleAiDecision Decision { get; } = decision;
        public AiStage Stage { get; set; }
        public int Target { get; set; } = decision.TargetSlot;
        public int MagicSlot { get; set; }
        public int Range { get; set; }
        public int Area { get; set; }
    }
    private AiPlan? _aiPlan;
    private BattleCombatant? _aiFailedActor;
    private int _aiFailedRound;
    private readonly Dictionary<BattleCombatant, BattleAiAction> _aiActions = [];
    private readonly Dictionary<BattleCombatant, int> _aiAttackTargets = [];
    private int _aiLegacySharedSlot;
    public BattleAiDecision? LastAiDecision { get; private set; }
    public BattleAiPrelude? LastAiPrelude { get; private set; }
    public string? LastAiTargetStrategy { get; private set; }
    public bool HasPendingAiContinuation => _aiPlan is not null;

    /// <summary>
    /// Historical DOS decision priority and typed movement continuations. One decision and
    /// one frozen prelude survive presentation; stepping while presenting never consumes RNG.
    /// Invalid legacy indices stop visibly instead of inventing an attack or changing magic.
    /// </summary>
    public bool StepEnemy()
    {
        if (IsPresenting) return false;
        if (CurrentActor is not { IsFriendly: false } actor) return Fail("Current actor is not an enemy.");
        if (ReferenceEquals(actor, _aiFailedActor) && _aiFailedRound == Round) return false;
        try
        {
            if (_aiPlan is null || !ReferenceEquals(_aiPlan.Actor, actor))
            {
                var prelude = BattleAiRules.Prelude(Combatants, actor);
                var choice = BattleAiRules.Choose(Combatants, actor, _magics, _items, prelude, _random,
                    other => _aiActions.GetValueOrDefault(other));
                _aiPlan = new(actor, prelude, choice); LastAiDecision = choice; LastAiPrelude = prelude;
                LastAiTargetStrategy = null;
                if (choice.Action is BattleAiAction.UsePoison or BattleAiAction.Detox or BattleAiAction.Medicine or BattleAiAction.Item or
                    BattleAiAction.RequestMedicine or BattleAiAction.RequestDetox or BattleAiAction.ThrowingWeapon)
                    _aiActions[actor] = choice.Action;
                if (choice.TargetSlot >= 0) _aiLegacySharedSlot = choice.TargetSlot;
            }
            for (var transitions = 0; transitions < 16; transitions++)
            {
                var plan = _aiPlan!;
                switch (plan.Stage)
                {
                    case AiStage.Dispatch:
                        switch (plan.Decision.Action)
                        {
                            case BattleAiAction.None: case BattleAiAction.Wait: return FinishAi(Rest);
                            case BattleAiAction.Attack: plan.Stage = AiStage.Attack; continue;
                            case BattleAiAction.Escape:
                                if (MoveAi(plan, BattleAiRules.EscapeDestination(Battlefield, Combatants, actor), 0, 0, AiStage.EscapeMoved)) return true;
                                continue;
                            case BattleAiAction.Item:
                                if (MoveAi(plan, BattleAiRules.EscapeDestination(Battlefield, Combatants, actor), 0, 0, AiStage.ItemMoved)) return true;
                                continue;
                            case BattleAiAction.RequestMedicine: case BattleAiAction.RequestDetox:
                                ValidateAiTarget(plan.Target);
                                if (MoveAi(plan, Combatants[plan.Target].Position, 0, 0, AiStage.RequestMoved)) return true;
                                continue;
                            case BattleAiAction.Medicine: case BattleAiAction.Detox:
                                ValidateAiTarget(plan.Target);
                                var support = plan.Decision.Action == BattleAiAction.Medicine ? BattleSupportAction.Medicine : BattleSupportAction.Detox;
                                plan.Range = BattleSupportRules.Range(actor.Role, support);
                                if (AiDistance(actor, Combatants[plan.Target].Position) <= plan.Range)
                                    return FinishAi(() => ApplyAiSupport(support, Combatants[plan.Target].Position));
                                if (MoveAi(plan, Combatants[plan.Target].Position, 1, plan.Range, AiStage.SupportMoved)) return true;
                                continue;
                            case BattleAiAction.UsePoison:
                                var poisonTarget = BattleAiRules.PoisonTarget(Combatants, actor, point => AiDistance(actor, point), _random, _aiLegacySharedSlot);
                                if (!poisonTarget.HasValue) { plan.Stage = AiStage.Attack; continue; }
                                plan.Target = poisonTarget.Value; _aiLegacySharedSlot = plan.Target; plan.Range = BattleSupportRules.Range(actor.Role, BattleSupportAction.UsePoison);
                                if (actor.RoundValue == 0 && AiDistance(actor, Combatants[plan.Target].Position) <= plan.Range)
                                    return FinishAi(() => ApplyAiSupport(BattleSupportAction.UsePoison, Combatants[plan.Target].Position));
                                if (MoveAi(plan, Combatants[plan.Target].Position, 3, plan.Range, AiStage.PoisonMoved)) return true;
                                continue;
                            case BattleAiAction.ThrowingWeapon:
                                plan.Target = SelectAiAttackTarget(actor); plan.Range = actor.Role[RoleWords.HiddenWeapon] / 15 + 1;
                                if (AiDistance(actor, Combatants[plan.Target].Position) <= plan.Range)
                                    return FinishAi(() => ThrowAiItem(plan.Decision.ItemId, Combatants[plan.Target].Position));
                                if (MoveAi(plan, Combatants[plan.Target].Position, 1, plan.Range, AiStage.ThrowMoved)) return true;
                                continue;
                            case BattleAiAction.Move:
                                ValidateAiTarget(_aiLegacySharedSlot);
                                if (MoveAi(plan, Combatants[_aiLegacySharedSlot].Position, 0, 0, AiStage.MoveFinished)) return true;
                                continue;
                            default: return FailAi("Unsupported native AI action.");
                        }
                    case AiStage.Attack:
                        var count = Enumerable.Range(0, RoleWords.MagicCount).Count(slot => actor.Role[RoleWords.MagicIdBegin + slot] > 0);
                        plan.MagicSlot = _random(count); // Native packed slot, not nth-positive or affordable/strongest selection.
                        var magicId = actor.Role[RoleWords.MagicIdBegin + plan.MagicSlot];
                        if (magicId <= 0 || !_magics.TryGetValue(magicId, out var magic)) return FailAi("Native packed magic selection references an unavailable slot.");
                        var profile = BattleMagicRules.Profile(actor.Role, magic, plan.MagicSlot);
                        plan.Range = profile.SelectDistance; plan.Area = profile.AreaType; plan.Target = SelectAiAttackTarget(actor);
                        if (AiAttackInRange(plan)) return ExecuteAiAttack(plan);
                        if (actor.RoundValue <= 0) return FinishAiWithoutEffect();
                        if (MoveAi(plan, Combatants[plan.Target].Position, plan.Area is 1 or 2 ? 2 : 1, plan.Range, AiStage.AttackMoved)) return true;
                        continue;
                    case AiStage.AttackMoved:
                        if (AiAttackInRange(plan)) return ExecuteAiAttack(plan);
                        var nearest = NearestAiTarget(actor, _aiAttackTargets.GetValueOrDefault(actor));
                        _aiAttackTargets[actor] = nearest; _aiLegacySharedSlot = nearest; plan.Target = nearest;
                        LastAiTargetStrategy = "nearest-after-move";
                        return AiAttackInRange(plan) ? ExecuteAiAttack(plan) : FinishAi(Rest);
                    case AiStage.PoisonMoved:
                        if (AiDistance(actor, Combatants[plan.Target].Position) <= plan.Range)
                            return FinishAi(() => ApplyAiSupport(BattleSupportAction.UsePoison, Combatants[plan.Target].Position));
                        if (AiAttackFallback(plan)) { plan.Stage = AiStage.Attack; continue; }
                        return FinishAi(Rest);
                    case AiStage.SupportMoved:
                        var action = plan.Decision.Action == BattleAiAction.Medicine ? BattleSupportAction.Medicine : BattleSupportAction.Detox;
                        if (AiDistance(actor, Combatants[plan.Target].Position) <= plan.Range)
                            return FinishAi(() => ApplyAiSupport(action, Combatants[plan.Target].Position));
                        if (AiAttackFallback(plan)) { plan.Stage = AiStage.Attack; continue; }
                        return FinishAi(Rest);
                    case AiStage.ThrowMoved:
                        if (AiDistance(actor, Combatants[plan.Target].Position) <= plan.Range)
                            return FinishAi(() => ThrowAiItem(plan.Decision.ItemId, Combatants[plan.Target].Position));
                        plan.Stage = AiStage.Attack; continue;
                    case AiStage.ItemMoved: return FinishAi(() => UseAiBattleItem(plan.Decision.ItemId));
                    case AiStage.EscapeMoved: return FinishAi(Rest);
                    case AiStage.RequestMoved: plan.Stage = AiStage.Attack; continue;
                    case AiStage.MoveFinished: return FinishAiWithoutEffect();
                    default: return FailAi("Unknown AI continuation stage.");
                }
            }
            return FailAi("AI continuation exceeded its bounded stage chain.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException)
        { return FailAi(error.Message); }
    }
    private int AiDistance(BattleCombatant actor, GridPosition target) => BattlePathing.Distances(Battlefield, Combatants,
        actor.Position, targeting: true).GetValueOrDefault(target, 555);
    private void ValidateAiTarget(int slot)
    { if (slot < 0 || slot >= Combatants.Count) throw new ArgumentException("Native stale AI target is outside combatants."); }
    private int SelectAiAttackTarget(BattleCombatant actor)
    {
        var distances = BattlePathing.Distances(Battlefield, Combatants, actor.Position, targeting: true);
        var choice = BattleAiRules.AttackTarget(Combatants, actor, point => distances.GetValueOrDefault(point, 555), _random, _aiAttackTargets.GetValueOrDefault(actor));
        ValidateAiTarget(choice.Slot); _aiAttackTargets[actor] = choice.Slot; _aiLegacySharedSlot = choice.Slot;
        LastAiTargetStrategy = choice.Strategy; return choice.Slot;
    }
    private int NearestAiTarget(BattleCombatant actor, int stale)
    {
        var best = 1000; var selected = stale;
        var distances = BattlePathing.Distances(Battlefield, Combatants, actor.Position, targeting: true);
        foreach (var person in Combatants)
            if (person.Side != actor.Side && !person.Hidden && distances.GetValueOrDefault(person.Position, 555) is var distance && distance < best)
            { best = distance; selected = person.Slot; }
        ValidateAiTarget(selected); return selected;
    }
    private bool AiAttackInRange(AiPlan plan)
    {
        ValidateAiTarget(plan.Target); var actor = plan.Actor; var target = Combatants[plan.Target];
        return AiDistance(actor, target.Position) <= plan.Range && (plan.Area is 0 or 3 ||
            plan.Area is 1 or 2 && (actor.Position.X == target.Position.X || actor.Position.Y == target.Position.Y));
    }
    private bool ExecuteAiAttack(AiPlan plan) => FinishAi(() => AttackAiMagic(plan.MagicSlot, Combatants[plan.Target].Position,
        BattleMagicRules.Facing(plan.Actor.Position, Combatants[plan.Target].Position)));
    private static bool AiAttackFallback(AiPlan plan) => plan.Prelude.AlliedCount > 0 &&
        2L * plan.Actor.Role[RoleWords.Attack] > 2L * plan.Prelude.AlliedTotal / plan.Prelude.AlliedCount;
    private bool MoveAi(AiPlan plan, GridPosition? requested, int mode, int range, AiStage next)
    {
        plan.Stage = next;
        if (requested is null) return false;
        var destination = BattleAiRules.MovementDestination(Battlefield, Combatants, plan.Actor, requested.Value, mode, range);
        if (destination is null) return false;
        // No move or a delegated failed move still reaches the native post-move check.
        return TryMoveAi(destination.Value) && IsPresenting;
    }
    private bool FinishAi(Func<bool> action)
    {
        var result = action();
        if (!result) return FailAi(LastError ?? "Native AI action could not execute.");
        _aiPlan = null; return true;
    }
    private bool FinishAiWithoutEffect()
    {
        LastDamage = []; LastError = null; _aiPlan = null; EndAction(); return true;
    }
    private bool FailAi(string message)
    {
        _aiFailedActor = CurrentActor; _aiFailedRound = Round; _aiPlan = null;
        return Fail("Native enemy AI stopped safely: " + message);
    }
}
