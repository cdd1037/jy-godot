using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public enum BattleSupportAction { UsePoison, Detox, Medicine }
public sealed record BattlePoisonResult(int Poison, int Amount);
public sealed record BattleMedicineResult(int Hp, int Hurt, int PhysicalPower, int Amount);

/// <summary>
/// Original DOS support kernels, without OpenLegend's later NG+ formulas.
/// Evidence: Z_DAT 0x32E59, 0x39776, 0x39A45, 0x39B1F, 0x39DA3,
/// 0x39E88 and 0x3A10C historical machine contracts. Results do not mutate roles.
/// </summary>
public static class BattleSupportRules
{
    private static int Word(long value) => unchecked((short)value);
    public static int SkillWord(BattleSupportAction action) => action switch
    {
        BattleSupportAction.UsePoison => RoleWords.UsePoison,
        BattleSupportAction.Detox => RoleWords.Detoxification,
        BattleSupportAction.Medicine => RoleWords.Medicine,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    public static int Range(RoleState actor, BattleSupportAction action) => actor[SkillWord(action)] / 15 + 1;
    public static bool CanUse(RoleState actor, BattleSupportAction action) => actor[SkillWord(action)] >= 20 &&
        actor[RoleWords.PhysicalPower] > (action == BattleSupportAction.UsePoison ? 10 : 50);

    public static BattlePoisonResult UsePoison(RoleState actor, RoleState target)
    {
        var amount = (int)Math.Clamp(((long)actor[RoleWords.UsePoison] - target[RoleWords.AntiPoison]) / 4, 0, 99);
        var before = target[RoleWords.Poison];
        if ((long)before + amount > 99) amount = Word(99L - before);
        return new(Math.Clamp(Word((long)before + amount), 0, 99), amount);
    }

    public static BattlePoisonResult Detox(RoleState actor, RoleState target, Func<int, int> random)
    {
        var ability = actor[RoleWords.Detoxification]; var before = target[RoleWords.Poison];
        // Both draws precede the strict threshold and target cap, even if treatment fails.
        var first = Roll(random, 10); var second = Roll(random, 10);
        var amount = Math.Clamp(Word((long)ability / 3 + first - second), 0, 99);
        if (before > (long)ability + 20) amount = 0;
        if (amount > before) amount = Word(before);
        var poison = Word((long)before - amount);
        if (poison < 0) poison = 0;
        if (poison > 100) poison = 99; // Exactly 100 is preserved by the original strict comparison.
        return new(poison, amount);
    }

    public static BattleMedicineResult Medicine(RoleState actor, RoleState target, Func<int, int> random)
    {
        var physical = actor[RoleWords.PhysicalPower]; var hp = target[RoleWords.Hp]; var hurt = target[RoleWords.Hurt];
        if (physical < 50) return new(hp, hurt, physical, 0);
        var rawMedicine = actor[RoleWords.Medicine]; long medicine = Math.Max(rawMedicine, 0);
        var amount = hurt <= 25 ? 4 * medicine / 5 : hurt <= 50 ? 3 * medicine / 4 : hurt <= 75 ? 2 * medicine / 3 : medicine / 2;
        amount += Roll(random, 5);
        if (hurt > (long)rawMedicine + 20) { amount = 0; medicine = 0; }
        var maximumHp = target[RoleWords.MaximumHp];
        if (hp + amount > maximumHp) amount = (long)maximumHp - hp;
        // Return full EAX, while HP/hurt/physical writes retain original word semantics.
        var result = checked((int)amount);
        hp = Word(hp + amount); if (hp > maximumHp) hp = maximumHp;
        hurt = Word(hurt - medicine); if (hurt < 0) hurt = 0;
        return new(hp, hurt, Word((long)physical - 2), result);
    }

    public static int FinishPhysicalPower(int physical) => Math.Max(Word((long)physical - 2), 0);
    private static int Roll(Func<int, int> random, int bound)
    {
        ArgumentNullException.ThrowIfNull(random);
        var value = random(bound);
        return value >= 0 && value < bound ? value : throw new ArgumentException("Support random source returned an invalid value.");
    }
}
