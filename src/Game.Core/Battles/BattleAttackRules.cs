using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed record BattleHpHit(int Hp, int Hurt, int Poison, int Damage, int CostScale, int RewardExperience);
public sealed record BattleMpHit(int ActorMp, int ActorMaximumMp, int TargetMp, int Damage);
public sealed record BattleAttackIteration(int Proficiency, bool LeveledUp, int Mp, int RewardExperience = 2);

/// <summary>
/// Pure original DOS numeric kernels, not OpenLegend's later NG+ replacements.
/// References: research/evidence/functions/Z_DAT/0x39188.md, 0x395EC.md,
/// 0x37734.md (historical machine contracts). Results are applied by BattleSession.
/// </summary>
public static class BattleAttackRules
{
    public static int Word(int value) => unchecked((short)value);
    private static int Roll(Func<int,int> random, int bound)
    {
        if (bound <= 1 || bound > 30000) return 0;
        var value = random(bound);
        return value >= 0 && value < bound ? value : throw new ArgumentException("Random source returned an out-of-range value.");
    }
    private static int LevelIndex(int proficiency) => proficiency is >= 0 and <= 999 ? proficiency/100 :
        throw new ArgumentException("Only the original ten magic levels are supported.");

    public static (int Attack, int Defence) EquipmentBonuses(RoleState role, IReadOnlyDictionary<int, ItemDefinition> items)
    {
        var attack = 0; var defence = 0;
        for (var slot = 0; slot < 2; slot++)
        {
            var id = role[RoleWords.EquipmentBegin+slot];
            if (id < 0) continue;
            if (!items.TryGetValue(id, out var item)) throw new ArgumentException($"Missing equipped item {id}.");
            attack = Word(attack+item[ItemWords.AddAttack]);
            defence = Word(defence+item[ItemWords.AddDefence]);
        }
        return (attack,defence);
    }

    public static int SpecialBonus(int weapon, int magicId) => (weapon,magicId) switch
    {
        (106,57) => 100, (107,49) or (108,49) => 50, (110,54) => 80,
        (115,63) => 50, (116,67) => 70, (119,68) => 100, _ => 0
    };

    public static int KnowledgeBonus(IEnumerable<RoleState> visibleRoles) => visibleRoles
        .Where(role => role[RoleWords.Hp] > 0 && role[RoleWords.Knowledge] > 80)
        .Sum(role => 2*role[RoleWords.Knowledge]);

    public static BattleHpHit HpDamage(RoleState actor, RoleState target, MagicDefinition magic, int proficiency,
        int distance, Func<int,int> random, int attackEquipment = 0, int defenceEquipment = 0,
        int specialBonus = 0, int alliedKnowledge = 0, int enemyKnowledge = 0)
    {
        var learned = LevelIndex(proficiency);
        var need = magic[MagicWords.NeedMp];
        if (need < 0 || actor[RoleWords.Mp] < 0 || distance is < 0 or > 126)
            throw new ArgumentException("HP attack MP, distance or cost is invalid.");
        var level = learned;
        while (level > 0 && actor[RoleWords.Mp] < need*((level+1)/2)) level--;
        var scale = level+1;
        var attack = Word((3*actor[RoleWords.Attack]+magic[MagicWords.AttackBegin+level])/2);
        attack = Word(attack+attackEquipment+specialBonus);
        attack = Word(attack+alliedKnowledge);
        var defence = Word(target[RoleWords.Defence]+defenceEquipment+enemyKnowledge);
        var damage = Word(2*(attack-3*defence)/3+Roll(random,20)-Roll(random,20));
        if (damage <= 0) damage = Word(attack/10+Roll(random,4)-Roll(random,4));
        if (damage < 0) damage = 0; // This branch skips physical/hurt/distance adjustments.
        else
        {
            damage = Word(damage+actor[RoleWords.PhysicalPower]/15+target[RoleWords.Hurt]/20);
            var factor = distance > 10 ? 2 : 100-3*(distance-1);
            var divisor = distance > 10 ? 3u : 100u;
            damage = Word((int)(unchecked((uint)(damage*factor))/divisor));
        }
        if (damage < 1) damage = 1;
        var hp = Word(target[RoleWords.Hp]-damage);
        var reward = damage/5;
        if (hp < 0) { hp = 0; reward = Word(reward+10*target[RoleWords.Level]); }
        var hurt = Word(target[RoleWords.Hurt]+damage/10);
        if (hurt > 99) hurt = 99;
        var poison = target[RoleWords.Poison];
        var power = magic[MagicWords.WithPoison]*(learned+1)+actor[RoleWords.AttackWithPoison];
        var resistance = target[RoleWords.AntiPoison];
        if (power > resistance && resistance < 90)
        {
            poison = Word(poison+(power-resistance)/15);
            if (poison > 100) poison = 99;
            if (poison < 0) poison = 0;
        }
        return new(hp,hurt,poison,damage,scale,reward);
    }

    public static BattleMpHit MpDamage(RoleState actor, RoleState target, MagicDefinition magic, int proficiency,
        Func<int,int> random)
    {
        var level = LevelIndex(proficiency);
        var oldTargetMp = target[RoleWords.Mp];
        var actorVariance = Roll(random,3)-Roll(random,3);
        var addition = magic[MagicWords.AddMpBegin+level];
        var mp = Word(actor[RoleWords.Mp]+addition);
        var maximum = Word(actor[RoleWords.MaximumMp]+Roll(random,addition/2));
        if (maximum >= 999) maximum = 999;
        mp = Word(mp+actorVariance);
        if (mp >= maximum) mp = maximum;
        var targetMp = ReferenceEquals(actor, target) ? mp : oldTargetMp;
        var targetVariance = Roll(random,3)-Roll(random,3);
        targetMp = Word(targetMp-magic[MagicWords.HurtMpBegin+level]-targetVariance);
        if (targetMp <= 0) targetMp = 0;
        if (ReferenceEquals(actor, target)) mp = targetMp;
        return new(mp,maximum,targetMp,oldTargetMp-targetMp);
    }

    public static BattleAttackIteration FinishIteration(RoleState actor, int magicSlot, MagicDefinition magic,
        int lastHpCostScale, Func<int,int> random)
    {
        if (magicSlot is < 0 or >= RoleWords.MagicCount || actor[RoleWords.MagicIdBegin+magicSlot] != magic.Id)
            throw new ArgumentException("Invalid attack completion slot.");
        var before = unchecked((ushort)actor[RoleWords.MagicLevelBegin+magicSlot]);
        var proficiency = (int)unchecked((ushort)(before+Roll(random,2)+1));
        if (proficiency > 999) proficiency = 999;
        var cost = Word(lastHpCostScale)/2*magic[MagicWords.NeedMp];
        var mp = Word(actor[RoleWords.Mp]-cost);
        if (mp < 0) mp = 0;
        return new(proficiency,proficiency/100 > before/100,mp);
    }

    public static int FinishAttackPhysicalPower(int physicalPower) => Math.Max(Word(physicalPower-3),0);
}
