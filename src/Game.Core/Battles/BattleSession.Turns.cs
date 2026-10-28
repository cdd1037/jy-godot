using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed partial class BattleSession
{
    public int EffectiveSpeed(BattleCombatant actor)
    {
        var speed = actor.Role[RoleWords.Speed];
        for (var slot = 0; slot < 2; slot++)
        {
            var id = actor.Role[RoleWords.EquipmentBegin + slot];
            if (id < 0) continue;
            if (!_items.TryGetValue(id, out var item)) throw new ArgumentException($"Missing equipped item {id}.");
            speed = BattleAttackRules.Word(speed + item[ItemWords.AddSpeed]);
        }
        return speed;
    }
    private void BeginRound()
    {
        if (EvaluateOutcome() != BattleOutcome.Ongoing) return;
        // Native pairwise swapping is deliberately not replaced with stable sort.
        for (var first = 0; first < _combatants.Count - 1; first++)
        for (var second = first + 1; second < _combatants.Count; second++)
            if (EffectiveSpeed(_combatants[first]) < EffectiveSpeed(_combatants[second]))
                (_combatants[first], _combatants[second]) = (_combatants[second], _combatants[first]);
        foreach (var actor in _combatants)
        { actor.RoundValue = Math.Max(0, EffectiveSpeed(actor) / 15 - actor.Role[RoleWords.Hurt] / 40); actor.ActionDone = false; }
        RebuildOccupancy(); Round++; _actor = 0; Phase = BattlePhase.Acting;
        while (_actor < _combatants.Count && _combatants[_actor].Hidden) _actor++;
    }
    public BattleOutcome EvaluateOutcome()
    {
        if (IsPresenting) return Outcome;
        if (!_rosterComplete || Phase == BattlePhase.Finished) return Outcome;
        foreach (var actor in _combatants)
            if (actor.Hp <= 0) actor.Hidden = true;
        RebuildOccupancy();
        var friendly = _combatants.Any(actor => actor.IsFriendly && !actor.Hidden);
        var enemy = _combatants.Any(actor => !actor.IsFriendly && !actor.Hidden);
        Outcome = !enemy ? BattleOutcome.Victory : !friendly ? BattleOutcome.Defeat : BattleOutcome.Ongoing;
        if (Outcome != BattleOutcome.Ongoing) Phase = BattlePhase.Finished;
        return Outcome;
    }
    public bool Rest()
    {
        if (IsPresenting) return Fail("An action is still being presented.");
        if (CurrentActor is not { } actor) return Fail("No active fighter.");
        var before = CaptureCombatantSnapshots(); var actorSlot = actor.Slot;
        var gain = _random(3) + (actor.RoundValue == actor.Role[RoleWords.Speed] / 10 ? 3 : 2);
        var physical = Math.Min(100, actor.PhysicalPower + gain); actor.Role.Set(RoleWords.PhysicalPower, physical);
        if (physical >= 30)
        {
            var bound = physical / 10 - 2;
            actor.Role.Set(RoleWords.Hp, Math.Min(actor.MaximumHp, actor.Hp + _random(bound) + 3));
            actor.Role.Set(RoleWords.Mp, Math.Min(actor.Role[RoleWords.MaximumMp], actor.Mp + _random(bound) + 3));
        }
        LastDamage = []; QueueRestPresentation(before, actorSlot); EndAction(); LastError = null; return true;
    }
    public bool Wait()
    {
        if (IsPresenting) return Fail("An action is still being presented.");
        if (CurrentActor is not { } actor) return Fail("No active fighter.");
        _combatants.RemoveAt(_actor); _combatants.Add(actor); RebuildOccupancy();
        while (_actor < _combatants.Count && _combatants[_actor].Hidden) _actor++;
        if (_actor >= _combatants.Count) FinishRound();
        LastError = null; return true;
    }
    private void EndAction()
    {
        if (DeferEndActionForPresentation()) return;
        _combatants[_actor].ActionDone = true;
        if (EvaluateOutcome() != BattleOutcome.Ongoing) return;
        _actor++; while (_actor < _combatants.Count && _combatants[_actor].Hidden) _actor++;
        if (_actor >= _combatants.Count) FinishRound();
    }
    private void FinishRound()
    {
        ApplyRoundStatus();
        BeginRound();
    }
    private void ApplyRoundStatus()
    {
        // Original sub_3C563: hurt>0 bypasses poison's alive/stamina/hidden gate.
        // HP and stamina floor to 1 only when strictly negative; exact 0 stays 0.
        foreach (var actor in _combatants)
        {
            var role = actor.Role; var hurt = role[RoleWords.Hurt]; var poison = role[RoleWords.Poison];
            if (!(hurt > 0 || poison > 0 && actor.Hp > 0 && actor.PhysicalPower > 0 && !actor.Hidden)) continue;
            var hp = BattleAttackRules.Word(actor.Hp - hurt / 20);
            hp = BattleAttackRules.Word(hp - poison / 10);
            role.Set(RoleWords.Hp, hp < 0 ? 1 : hp);
            if (actor.PhysicalPower < 0) role.Set(RoleWords.PhysicalPower, 1);
        }
    }

    /// <summary>
    /// Original result/settlement callee returns before one final sub_3C563 call.
    /// The host invokes this once after settlement; the decided outcome is frozen.
    /// </summary>
    public bool CompleteAfterSettlement()
    {
        if (Phase != BattlePhase.Finished || _settlementCompleted) return false;
        ApplyRoundStatus(); _settlementCompleted = true; return true;
    }
}
