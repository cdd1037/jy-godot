using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed record BattleAiTargetSelection(int Slot, string Strategy, bool Written);

public static partial class BattleAiRules
{
    public static BattleAiTargetSelection AttackTarget(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor,
        Func<GridPosition, int> distance, Func<int, int> random, int staleSlot)
    {
        var candidates = persons.Where(person => person.Side != actor.Side && !person.Hidden).ToArray();
        var selected = staleSlot; var written = false; var strategy = "nearest";
        void Extremum(Func<BattleCombatant, int> value, int initial, bool maximum)
        {
            var best = initial;
            foreach (var candidate in candidates)
                if (maximum ? value(candidate) > best : value(candidate) < best)
                { best = value(candidate); selected = candidate.Slot; written = true; }
        }
        var role = actor.Role;
        if (role[RoleWords.Morality] >= 75 && random(10) < 7) { strategy = "strongest"; Extremum(person => person.Role[RoleWords.Attack], 0, true); }
        else if (role[RoleWords.Morality] <= 25 && random(10) < 7) { strategy = "weakest"; Extremum(person => person.Role[RoleWords.Attack], 1000, false); }
        else if (role[RoleWords.Iq] >= 70 && random(10) < 7)
        {
            strategy = "specialist"; var best = 0; var detoxFlag = false; var medicineFlag = false;
            if (persons.Any(person => person.Side == actor.Side && person.Role[RoleWords.UsePoison] > 20))
                foreach (var candidate in candidates) if (candidate.Role[RoleWords.Detoxification] > best)
                { best = candidate.Role[RoleWords.Detoxification]; selected = candidate.Slot; written = true; if (best >= 20) detoxFlag = true; }
            if (!detoxFlag)
                foreach (var candidate in candidates) if (candidate.Role[RoleWords.Medicine] > best)
                { best = candidate.Role[RoleWords.Medicine]; selected = candidate.Slot; written = true; if (best >= 20) medicineFlag = true; }
            // Original 35217 checks only medicineFlag: a detox specialist still falls back.
            if (!medicineFlag) { written = false; Extremum(person => person.Role[RoleWords.Attack], 1000, false); }
        }
        else Extremum(person => distance(person.Position), 1000, false);
        return new(selected, strategy, written);
    }

    public static int? PoisonTarget(IReadOnlyList<BattleCombatant> persons, BattleCombatant actor,
        Func<GridPosition, int> distance, Func<int, int> random, int legacySharedSlot)
    {
        var eligible = persons.Where(person => person.Side != actor.Side && !person.Hidden &&
            person.Role[RoleWords.Poison] < 95 && person.Role[RoleWords.AntiPoison] < actor.Role[RoleWords.UsePoison]).ToArray();
        if (actor.Role[RoleWords.Iq] > 60 && random(10) < 7)
        {
            var best = 0; int? target = null;
            foreach (var person in eligible) if (person.Role[RoleWords.Attack] > best) { best = person.Role[RoleWords.Attack]; target = person.Slot; }
            if (target.HasValue) return target;
        }
        if (eligible.Length == 0) return null;
        if (legacySharedSlot < 0 || legacySharedSlot >= persons.Count) throw new ArgumentException("Legacy poison target scratch is outside combatants.");
        // 3570F's intentional stale-distance bug selects the first eligible slot, not the nearest.
        return distance(persons[legacySharedSlot].Position) < 1000 ? eligible[0].Slot : null;
    }

    public static GridPosition? EscapeDestination(BattlefieldDefinition field, IReadOnlyList<BattleCombatant> persons, BattleCombatant actor)
    {
        var distances = BattlePathing.Distances(field, persons, actor.Position); var score = 0; GridPosition? selected = null;
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
        {
            var point = new GridPosition(x, y); if (!distances.TryGetValue(point, out var value) || value != actor.RoundValue) continue;
            var total = persons.Where(person => person.Side != actor.Side).Sum(person => Manhattan(point, person.Position));
            if (total > score) { score = total; selected = point; }
        }
        return selected;
    }
    public static int Manhattan(GridPosition a, GridPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>3650E destination and stop modes, using the existing bounded BFS safety adaptation.</summary>
    public static GridPosition? MovementDestination(BattlefieldDefinition field, IReadOnlyList<BattleCombatant> persons,
        BattleCombatant actor, GridPosition requested, int mode, int range)
    {
        if (mode is < 0 or > 3 || !BattlefieldDefinition.Contains(requested) || range < 0 || actor.RoundValue <= 0) return null;
        var fromActor = BattlePathing.Distances(field, persons, actor.Position);
        var fromTarget = BattlePathing.Distances(field, persons, requested, targeting: true);
        var within = fromTarget.GetValueOrDefault(actor.Position, 555) - actor.RoundValue <= range;
        GridPosition? destination = requested;
        if (mode == 3 || mode == 2 && within)
        {
            var layers = BattlePathing.Distances(field, persons, requested);
            // No valid 64x64 shortest path exceeds 4095; skip absent high layers without legacy wrap loops.
            for (var layer = Math.Min(range, 4095); layer >= 1; layer--)
            {
                var best = 1000; GridPosition? candidate = null;
                for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
                {
                    var point = new GridPosition(x, y);
                    if (!layers.TryGetValue(point, out var value) || value != layer || mode == 2 && point.X != requested.X && point.Y != requested.Y) continue;
                    var distance = Manhattan(point, actor.Position);
                    if (distance < best) { best = distance; candidate = point; }
                }
                if (candidate is not null) { destination = candidate; break; }
            }
        }
        else
        {
            var cursor = requested;
            for (var iteration = 0; iteration < 4096; iteration++)
            {
                GridPosition? candidate = null;
                foreach (var aligned in new[] { true, false })
                {
                    foreach (var direction in Enum.GetValues<Direction>())
                    {
                        var point = cursor.Step(direction);
                        if (fromActor.TryGetValue(point, out var distance) && distance < 128 && (!aligned || point.X == actor.Position.X || point.Y == actor.Position.Y))
                        { candidate = point; break; }
                    }
                    if (candidate is not null) break;
                }
                if (candidate is not null) { cursor = candidate.Value; break; }
                cursor = cursor.X != actor.Position.X ? new(cursor.X + Math.Sign(actor.Position.X - cursor.X), cursor.Y) :
                    new(cursor.X, cursor.Y + Math.Sign(actor.Position.Y - cursor.Y));
                if (cursor == actor.Position) break;
            }
            destination = cursor;
        }
        if (destination is null || destination == actor.Position || !fromActor.TryGetValue(destination.Value, out var fullDistance) || fullDistance >= 128) return null;
        var path = BattlePathing.FindPath(field, persons, actor.Position, destination.Value, maximum: 127);
        if (path is null) return null;
        var stop = Math.Min(actor.RoundValue, path.Count - 1);
        if (mode is 1 or 2)
            for (var index = 1; index <= stop; index++)
                if (Manhattan(path[index], requested) <= range && (mode == 1 || path[index].X == requested.X || path[index].Y == requested.Y)) { stop = index; break; }
        return stop > 0 ? path[stop] : null;
    }
}
