using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed partial class BattleSession
{
    public IReadOnlyList<int> AvailableMagicSlots() => !IsPresenting && CurrentActor is { PhysicalPower: > 10 } actor
        ? BattleMagicRules.AvailableMagicSlots(actor.Role, _magics) : [];
    public bool CanTargetMagic(int magicSlot, GridPosition target)
    {
        if (CurrentActor is not { } actor || !AvailableMagicSlots().Contains(magicSlot)) return false;
        var magic = _magics[actor.Role[RoleWords.MagicIdBegin + magicSlot]];
        return TargetInRange(actor, BattleMagicRules.Profile(actor.Role, magic, magicSlot), target);
    }
    public IReadOnlyList<BattleAreaCell> AttackArea(int magicSlot, GridPosition target, Direction direction = Direction.Up)
    {
        var actor = CurrentActor ?? throw new InvalidOperationException("No active fighter.");
        var id = actor.Role[RoleWords.MagicIdBegin + magicSlot];
        return BattleMagicRules.AreaCells(BattleMagicRules.Profile(actor.Role, _magics[id], magicSlot), actor.Position, target, direction);
    }
    private bool TargetInRange(BattleCombatant actor, BattleAttackProfile profile, GridPosition target)
    {
        if (!BattlefieldDefinition.Contains(target)) return false;
        if (profile.AreaType is 1 or 2) return true;
        var distances = BattlePathing.Distances(Battlefield, Combatants, actor.Position, targeting: true, maximum: profile.SelectDistance);
        return distances.TryGetValue(target, out var distance) && distance <= profile.SelectDistance;
    }

    public bool AttackMagic(int magicSlot, GridPosition target, Direction direction = Direction.Up) =>
        ExecuteMagic(magicSlot, target, direction, automatic: false);

    internal bool AttackAiMagic(int magicSlot, GridPosition target, Direction direction = Direction.Up) =>
        ExecuteMagic(magicSlot, target, direction, automatic: true);

    private bool ExecuteMagic(int magicSlot, GridPosition target, Direction direction, bool automatic)
    {
        if (IsPresenting) return Fail("An action is still being presented.");
        if (CurrentActor is not { } actor) return Fail("No active fighter.");
        try
        {
            if (automatic ? actor.IsFriendly || magicSlot is < 0 or >= RoleWords.MagicCount ||
                !_magics.ContainsKey(actor.Role[RoleWords.MagicIdBegin + magicSlot]) : !AvailableMagicSlots().Contains(magicSlot))
                return Fail("This learned magic is not currently available.");
            var magic = _magics[actor.Role[RoleWords.MagicIdBegin + magicSlot]];
            var profile = BattleMagicRules.Profile(actor.Role, magic, magicSlot);
            if (!TargetInRange(actor, profile, target)) return Fail("Attack target is outside the targeting path range.");
            ValidateMagicPresentation(actor, magic);
            var cells = BattleMagicRules.AreaCells(profile, actor.Position, target, direction);
            var damageEvents = new List<BattleDamageEvent>();
            actor.Facing = profile.AreaType == 1 ? direction : BattleMagicRules.Facing(actor.Position, target);
            for (var iteration = 0; iteration < profile.AttackCount; iteration++)
            {
                var before = CaptureCombatantSnapshots(); var actorSlot = actor.Slot; var damageStart = damageEvents.Count;
                foreach (var cell in cells)
                {
                    var victim = OccupantAt(cell.Position);
                    if (victim is null || victim.Hidden || victim.IsFriendly == actor.IsFriendly) continue;
                    var proficiency = actor.Role[RoleWords.MagicLevelBegin + magicSlot];
                    if (cell.ForceHp || profile.HurtType == 0)
                    {
                        var offense = BattleAttackRules.EquipmentBonuses(actor.Role, _items);
                        var defense = BattleAttackRules.EquipmentBonuses(victim.Role, _items);
                        var alliedKnowledge = BattleAttackRules.KnowledgeBonus(_combatants.Where(person => !person.Hidden && person.IsFriendly == actor.IsFriendly).Select(person => person.Role));
                        var enemyKnowledge = BattleAttackRules.KnowledgeBonus(_combatants.Where(person => !person.Hidden && person.IsFriendly != actor.IsFriendly).Select(person => person.Role));
                        var hit = BattleAttackRules.HpDamage(actor.Role, victim.Role, magic, proficiency, cell.Distance, _random,
                            offense.Attack, defense.Defence, BattleAttackRules.SpecialBonus(actor.Role[RoleWords.EquipmentBegin], magic.Id), alliedKnowledge, enemyKnowledge);
                        victim.Role.Set(RoleWords.Hp, hit.Hp); victim.Role.Set(RoleWords.Hurt, hit.Hurt); victim.Role.Set(RoleWords.Poison, hit.Poison);
                        HpCostScale = hit.CostScale; actor.RewardExperience = BattleAttackRules.Word(actor.RewardExperience + hit.RewardExperience);
                        damageEvents.Add(new(actor.Slot, victim.Slot, cell.Position, hit.Damage, false));
                    }
                    else
                    {
                        var hit = BattleAttackRules.MpDamage(actor.Role, victim.Role, magic, proficiency, _random);
                        actor.Role.Set(RoleWords.Mp, hit.ActorMp); actor.Role.Set(RoleWords.MaximumMp, hit.ActorMaximumMp); victim.Role.Set(RoleWords.Mp, hit.TargetMp);
                        damageEvents.Add(new(actor.Slot, victim.Slot, cell.Position, hit.Damage, true));
                    }
                }
                var finish = BattleAttackRules.FinishIteration(actor.Role, magicSlot, magic, HpCostScale, _random);
                actor.Role.Set(RoleWords.MagicLevelBegin + magicSlot, finish.Proficiency); actor.Role.Set(RoleWords.Mp, finish.Mp);
                actor.RewardExperience = BattleAttackRules.Word(actor.RewardExperience + finish.RewardExperience);
                QueueMagicPresentation(before, actorSlot, magicSlot, target, direction, damageEvents.Skip(damageStart).ToArray());
            }
            actor.Role.Set(RoleWords.PhysicalPower, BattleAttackRules.FinishAttackPhysicalPower(actor.PhysicalPower));
            LastDamage = damageEvents.AsReadOnly(); EndAction(); LastError = null; return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException)
        { return Fail(error.Message); }
    }
}
