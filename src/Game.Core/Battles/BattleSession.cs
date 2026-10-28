using System.Collections.ObjectModel;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

/// <summary>
/// Host-independent tactical session. Native contracts: OpenLegend battle_setup.cpp,
/// battle_pathing.cpp and research/evidence/battle-1to1.md §§6–8,18, sub_3C563.
/// Friendly state is shared; enemy baselines are isolated from the party. No
/// pre-battle party healing, invented normal attack, or automatic victory.
/// </summary>
public sealed partial class BattleSession
{
    public const int Capacity = 26;
    public const string AiPolicyDescription = "DOS action priorities, carried items, support, packed magic and role-based targeting with movement continuations; unsafe legacy indices and path aliases are rejected.";
    private readonly PartyState _party;
    private readonly IReadOnlyDictionary<int, MagicDefinition> _magics;
    private readonly IReadOnlyDictionary<int, ItemDefinition> _items;
    private readonly List<BattleCombatant> _combatants = [];
    private readonly Dictionary<int, RoleState> _enemies = [];
    private readonly Func<int, int> _random;
    private readonly IBattleInventory? _battleInventory;
    private readonly HashSet<int> _mandatory = [];
    private readonly int[] _occupancy = Enumerable.Repeat(-1, 4096).ToArray();
    private int _actor;
    private uint _randomState;
    private bool _rosterComplete, _settlementCompleted;

    public BattleSession(BattleDefinition definition, BattlefieldDefinition battlefield, PartyState party,
        IReadOnlyDictionary<int, MagicDefinition> magics, IReadOnlyDictionary<int, ItemDefinition>? items = null,
        uint randomSeed = 1, int initialCostScale = 0, Func<int, int>? random = null, IBattleInventory? inventory = null,
        bool presentActions = false)
    {
        if (definition.BattlefieldId != battlefield.Id) throw new ArgumentException("Battlefield ID does not match the battle definition.");
        Definition = definition; Battlefield = battlefield; _party = party; _magics = magics;
        _items = items ?? new Dictionary<int, ItemDefinition>();
        _battleInventory = inventory;
        _presentActions = presentActions;
        _randomState = randomSeed;
        _random = bound =>
        {
            if (bound <= 1 || bound > 30000) return 0;
            var value = random is null ? DrawRandom(bound) : random(bound);
            return value >= 0 && value < bound ? value : throw new ArgumentException("Battle random source returned an invalid value.");
        };
        HpCostScale = initialCostScale;
        Combatants = _combatants.AsReadOnly();
        SelectableRoleIds = Array.AsReadOnly(party.Members.Where(id => id >= 0).ToArray());
        if (Enumerable.Range(0, 6).Any(slot => definition.FixedParty(slot) != -1))
        {
            for (var slot = 0; slot < 6; slot++)
                if (definition.FixedParty(slot) != -1) Append(definition.FixedParty(slot), true, definition.PartyPosition(slot));
            AppendEnemies(); BeginRound();
        }
        else
        {
            for (var slot = 0; slot < 6; slot++)
                if (definition.PresetParty(slot) != -1)
                {
                    var id = definition.PresetParty(slot); _mandatory.Add(id);
                    Append(id, true, definition.PartyPosition(slot));
                }
            Phase = BattlePhase.PartySelection;
        }
        MandatoryRoleIds = Array.AsReadOnly(_mandatory.Order().ToArray());
    }
    public BattleDefinition Definition { get; }
    public BattlefieldDefinition Battlefield { get; }
    public IReadOnlyList<BattleCombatant> Combatants { get; }
    public IReadOnlyList<int> SelectableRoleIds { get; }
    public IReadOnlyList<int> MandatoryRoleIds { get; }
    public BattlePhase Phase { get; private set; }
    public BattleOutcome Outcome { get; private set; }
    public BattleCombatant? CurrentActor => Phase == BattlePhase.Acting && _actor < _combatants.Count ? _combatants[_actor] : null;
    public int Round { get; private set; }
    public int HpCostScale { get; private set; }
    public uint RandomState => _randomState;
    public string? LastError { get; private set; }
    public IReadOnlyList<BattleDamageEvent> LastDamage { get; private set; } = [];

    public bool ConfirmParty(IReadOnlyCollection<int> selectedRoleIds)
    {
        if (IsPresenting) return Fail("An action is still being presented.");
        if (Phase != BattlePhase.PartySelection) return Fail("Party selection is already complete.");
        if (selectedRoleIds.Any(id => !SelectableRoleIds.Contains(id))) return Fail("Selected role is outside the party.");
        var selected = _party.Members.Where(id => selectedRoleIds.Contains(id) && !_mandatory.Contains(id)).ToArray();
        if (_combatants.Count + selected.Length is < 1 or > 6) return Fail("Select between one and six fighters including mandatory roles.");
        // Validate the entire selection before writing occupancy.
        foreach (var pair in selected.Select((id, index) => (id, index)))
            ValidateRoleAndPosition(pair.id, Definition.PartyPosition(_combatants.Count + pair.index));
        for (var slot = 0; slot < 20; slot++)
            if (Definition.EnemyRole(slot) != -1) ValidateRoleAndPosition(Definition.EnemyRole(slot), Definition.EnemyPosition(slot));
        foreach (var id in selected) Append(id, true, Definition.PartyPosition(_combatants.Count));
        AppendEnemies(); BeginRound(); LastError = null; return true;
    }

    private void ValidateRoleAndPosition(int roleId, GridPosition position)
    {
        if (!_party.Roles.ContainsKey(roleId)) throw new ArgumentException($"Missing battle role {roleId}.");
        if (!BattlefieldDefinition.Contains(position)) throw new ArgumentException("Combatant position is outside the battlefield.");
    }
    private void Append(int roleId, bool friendly, GridPosition position)
    {
        ValidateRoleAndPosition(roleId, position);
        if (_combatants.Count >= Capacity) throw new ArgumentException("Battle exceeds 26 combatants.");
        RoleState role;
        if (friendly) role = _party.Roles[roleId];
        else if (!_enemies.TryGetValue(roleId, out role!))
        {
            role = new RoleState(_party.Roles[roleId].Definition);
            _enemies.Add(roleId, role);
        }
        _combatants.Add(new(_combatants.Count, role, friendly, position));
        RebuildOccupancy();
    }
    private void AppendEnemies()
    {
        for (var slot = 0; slot < 20; slot++)
            if (Definition.EnemyRole(slot) != -1) Append(Definition.EnemyRole(slot), false, Definition.EnemyPosition(slot));
        _rosterComplete = true;
    }
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
    private void RebuildOccupancy()
    {
        Array.Fill(_occupancy, -1);
        for (var slot = 0; slot < _combatants.Count; slot++)
        {
            var actor = _combatants[slot]; actor.Slot = slot;
            // Original duplicate coordinates intentionally use the last slot.
            if (!actor.Hidden) _occupancy[actor.Position.Y * 64 + actor.Position.X] = slot;
        }
    }
    public BattleCombatant? OccupantAt(GridPosition position) => BattlefieldDefinition.Contains(position) &&
        _occupancy[position.Y * 64 + position.X] is var slot && slot >= 0 ? _combatants[slot] : null;

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
    public IReadOnlyDictionary<GridPosition, int> ReachableMoves() => !IsPresenting && CurrentActor is { PhysicalPower: > 5, RoundValue: > 0 } actor
        ? BattlePathing.Distances(Battlefield, Combatants, actor.Position, maximum: actor.RoundValue)
        : new ReadOnlyDictionary<GridPosition, int>(new Dictionary<GridPosition, int>());
    public bool TryMove(GridPosition destination) => ExecuteMove(destination, automatic: false);
    internal bool TryMoveAi(GridPosition destination) => ExecuteMove(destination, automatic: true);

    private bool ExecuteMove(GridPosition destination, bool automatic)
    {
        if (IsPresenting) return Fail("An action is still being presented.");
        if (CurrentActor is not { } actor) return Fail("No active fighter.");
        if (actor.RoundValue <= 0 || (automatic ? actor.IsFriendly : actor.PhysicalPower <= 5)) return Fail("Movement is unavailable.");
        var path = BattlePathing.FindPath(Battlefield, Combatants, actor.Position, destination, actor.RoundValue);
        if (path is null || path.Count <= 1) return Fail("Destination is occupied, blocked or outside movement range.");
        var before = CaptureCombatantSnapshots(); var actorSlot = actor.Slot;
        foreach (var position in path.Skip(1))
        {
            if (actor.RoundValue == actor.Role[RoleWords.Speed] / 10)
                actor.Role.Set(RoleWords.PhysicalPower, Math.Max(0, actor.PhysicalPower - 1));
            actor.Facing = position.Y < actor.Position.Y ? Direction.Up : position.X > actor.Position.X ? Direction.Right :
                position.X < actor.Position.X ? Direction.Left : Direction.Down;
            actor.Position = position; actor.RoundValue--;
        }
        RebuildOccupancy(); QueueMovementPresentation(before, actorSlot, path); LastError = null; return true;
    }
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

    private int DrawRandom(int bound)
    {
        if (bound <= 1 || bound > 30000) return 0;
        _randomState = unchecked(_randomState * 0x41C64E6D + 0x3039);
        return (int)((_randomState >> 16) & 0x7FFF) % bound;
    }
    private bool Fail(string message) { LastError = message; return false; }
}
