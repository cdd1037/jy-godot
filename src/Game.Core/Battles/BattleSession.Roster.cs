using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed partial class BattleSession
{
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
}
