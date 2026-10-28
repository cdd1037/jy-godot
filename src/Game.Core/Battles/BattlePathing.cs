using JinYong.Core.Maps;

namespace JinYong.Core.Battles;

public static class BattlePathing
{
    private static readonly (int Low, int High)[] BlockedRanges =
    [(358,362),(374,380),(458,464),(506,610),(818,824),(838,838),(934,936),(1016,1022),(1324,1348)];
    public static bool GroundBlocked(int tile) => BlockedRanges.Any(range => tile >= range.Low && tile <= range.High);

    /// <summary>
    /// Native movement checks upper layer, occupied cells and nine ground ranges;
    /// targeting checks only upper layer. Source is always traversable. We use a
    /// bounded 64×64 BFS rather than DOS's 255-entry queue and x=64 aliasing.
    /// Reference: OpenLegend src/battle/battle_pathing.cpp, sub_36E06..37245.
    /// </summary>
    public static IReadOnlyDictionary<GridPosition, int> Distances(BattlefieldDefinition field,
        IReadOnlyList<BattleCombatant> combatants, GridPosition source, bool targeting = false, int maximum = int.MaxValue)
    {
        if (!BattlefieldDefinition.Contains(source)) throw new ArgumentOutOfRangeException(nameof(source));
        HashSet<GridPosition> occupied = targeting ? [] : combatants.Where(actor => !actor.Hidden).Select(actor => actor.Position).ToHashSet();
        var distances = new Dictionary<GridPosition, int> { [source] = 0 };
        var queue = new Queue<GridPosition>([source]);
        while (queue.TryDequeue(out var position))
        {
            if (distances[position] >= maximum) continue;
            foreach (var direction in Enum.GetValues<Direction>())
            {
                var next = position.Step(direction);
                if (!BattlefieldDefinition.Contains(next) || distances.ContainsKey(next) ||
                    field.At(BattlefieldLayer.Building, next) != 0 ||
                    !targeting && (occupied.Contains(next) || GroundBlocked(field.At(BattlefieldLayer.Ground, next)))) continue;
                distances.Add(next, distances[position] + 1); queue.Enqueue(next);
            }
        }
        return distances;
    }

    public static IReadOnlyList<GridPosition>? FindPath(BattlefieldDefinition field, IReadOnlyList<BattleCombatant> combatants,
        GridPosition source, GridPosition target, int maximum = int.MaxValue)
    {
        var distances = Distances(field, combatants, source, maximum: maximum);
        if (!distances.TryGetValue(target, out var distance)) return null;
        var path = new List<GridPosition> { target }; var cursor = target;
        while (distance > 0)
        {
            var previous = Enum.GetValues<Direction>().Select(cursor.Step)
                .First(point => distances.TryGetValue(point, out var value) && value == distance - 1);
            path.Add(previous); cursor = previous; distance--;
        }
        path.Reverse(); return path;
    }
}
