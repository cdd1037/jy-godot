using System.Collections.ObjectModel;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed partial class BattleSession
{
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
}
