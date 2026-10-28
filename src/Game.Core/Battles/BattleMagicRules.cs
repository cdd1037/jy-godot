using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed record BattleAttackProfile(int MagicSlot, int MagicId, int LevelIndex, int SelectDistance,
    int AttackDistance, int AreaType, int HurtType, int AttackCount, int NeedMp);
public readonly record struct BattleAreaCell(GridPosition Position, int Distance, bool ForceHp);

/// <summary>Original ten-level geometry. Reference: OpenLegend Z_DAT/0x37734.md historical machine contract.</summary>
public static class BattleMagicRules
{
    public static BattleAttackProfile Profile(RoleState actor, MagicDefinition magic, int magicSlot)
    {
        if (magicSlot is < 0 or >= RoleWords.MagicCount || actor[RoleWords.MagicIdBegin + magicSlot] != magic.Id || magic.Id <= 0)
            throw new ArgumentException("The selected slot does not contain this learned magic.");
        var proficiency = actor[RoleWords.MagicLevelBegin + magicSlot];
        if (proficiency is < 0 or > 999) throw new ArgumentException("Only original proficiency 0..999 is supported.");
        var index = proficiency / 100;
        var select = magic[MagicWords.SelectDistanceBegin + index];
        var radius = magic[MagicWords.AttackDistanceBegin + index];
        if (select is < 0 or > 63 || radius is < 0 or > 63 || magic[MagicWords.AttackAreaType] is < 0 or > 3 ||
            magic[MagicWords.HurtType] is < 0 or > 1 || magic[MagicWords.NeedMp] < 0)
            throw new ArgumentException("Unsupported original magic geometry, damage type or MP cost.");
        return new(magicSlot, magic.Id, index, select, radius, magic[MagicWords.AttackAreaType], magic[MagicWords.HurtType],
            actor[RoleWords.AttackTwice] == 1 ? 2 : 1, magic[MagicWords.NeedMp]);
    }

    public static int[] AvailableMagicSlots(RoleState actor, IReadOnlyDictionary<int, MagicDefinition> magics)
    {
        var result = new List<int>();
        for (var slot = 0; slot < RoleWords.MagicCount; slot++)
        {
            var id = actor[RoleWords.MagicIdBegin + slot];
            if (id <= 0) continue;
            if (!magics.TryGetValue(id, out var magic)) throw new ArgumentException($"Missing learned magic {id}.");
            _ = Profile(actor, magic, slot);
            if (actor[RoleWords.Mp] >= magic[MagicWords.NeedMp]) result.Add(slot);
        }
        return result.ToArray();
    }

    // Target reachability is checked by the session's upper-layer targeting map.
    // Area geometry itself ignores intervening occupancy and terrain.
    public static IReadOnlyList<BattleAreaCell> AreaCells(BattleAttackProfile profile, GridPosition actor,
        GridPosition target, Direction direction)
    {
        if (!SceneDefinition.Contains(actor) || !SceneDefinition.Contains(target) || !Enum.IsDefined(direction))
            throw new ArgumentException("Attack coordinates or direction are outside the battlefield.");
        if (profile.SelectDistance is < 0 or > 63 || profile.AttackDistance is < 0 or > 63 || profile.AreaType is < 0 or > 3)
            throw new ArgumentException("Attack geometry is invalid.");
        var cells = new List<BattleAreaCell>();
        void Cell(GridPosition position, int distance, bool hp)
        {
            if (SceneDefinition.Contains(position)) cells.Add(new(position, distance, hp));
        }
        if (profile.AreaType == 1)
        {
            var position = actor;
            for (var distance = 1; distance <= profile.SelectDistance; distance++)
            {
                position = position.Step(direction);
                Cell(position, distance, true);
            }
        }
        else if (profile.AreaType == 2)
            for (var distance = 1; distance <= profile.SelectDistance; distance++)
            {
                Cell(new(actor.X, actor.Y-distance), distance, true);
                Cell(new(actor.X, actor.Y+distance), distance, true);
                Cell(new(actor.X-distance, actor.Y), distance, true);
                Cell(new(actor.X+distance, actor.Y), distance, true);
            }
        else
            for (var x = target.X-profile.AttackDistance; x <= target.X+profile.AttackDistance; x++)
            for (var y = target.Y-profile.AttackDistance; y <= target.Y+profile.AttackDistance; y++)
                Cell(new(x,y), Math.Abs(actor.X-x)+Math.Abs(actor.Y-y), false);
        return cells.AsReadOnly();
    }

    public static Direction Facing(GridPosition actor, GridPosition target) => Math.Abs(target.X-actor.X) >= Math.Abs(target.Y-actor.Y)
        ? target.X <= actor.X ? Direction.Left : Direction.Right
        : target.Y <= actor.Y ? Direction.Up : Direction.Down;
}
