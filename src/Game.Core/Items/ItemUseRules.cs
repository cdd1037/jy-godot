using System.Collections.ObjectModel;
using JinYong.Core.Battles;
using JinYong.Core.Session;

namespace JinYong.Core.Items;

public sealed class ItemUseResult
{
    internal ItemUseResult(int roleId, int[] before, int[] after, int[] deltas)
    {
        RoleId = roleId; Words = Array.AsReadOnly(after); Deltas = Array.AsReadOnly(deltas);
        Changes = new ReadOnlyDictionary<int, int>(Enumerable.Range(0, before.Length)
            .Where(word => before[word] != after[word]).ToDictionary(word => word, word => after[word]));
    }
    public int RoleId { get; }
    public IReadOnlyList<int> Words { get; }
    public IReadOnlyList<int> Deltas { get; }
    public IReadOnlyDictionary<int, int> Changes { get; }
    public bool HasEffect => Deltas.Any(delta => delta != 0);
}

/// <summary>
/// Original DOS sub_2B483, shared by exploration and battle. References: read-only
/// OpenLegend research/evidence/functions/Z_DAT/0x2B483.md historical machine
/// contract. Current OpenLegend NG+ scaling, wide arithmetic and poison overflow
/// are deliberately excluded. Random comes from the caller's existing stream.
/// </summary>
public static class ItemUseRules
{
    private static int Word(int value) => unchecked((short)value);
    private static int Roll(Func<int, int> random, int bound)
    {
        var value = random(bound);
        return value >= 0 && value < bound ? value : throw new ArgumentException("Random source returned an invalid value.");
    }
    public static ItemUseResult Calculate(RoleState actor, RoleState target, ItemDefinition item, Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(item); ArgumentNullException.ThrowIfNull(random);
        if (target[RoleWords.Hurt] < 0 || target[RoleWords.MaximumHp] < 0 || target[RoleWords.MaximumMp] < 0)
            throw new ArgumentException("Item target has invalid HP, MP or hurt.");
        var before = Enumerable.Range(0, RoleWords.Count).Select(word => target[word]).ToArray();
        var words = before.ToArray(); var deltas = new int[23];
        var hpEffect = item[ItemWords.AddHp];
        if (hpEffect != 0)
        {
            int hpDelta;
            if (hpEffect > 0)
            {
                hpDelta = Word(hpEffect - words[RoleWords.Hurt] / 2 + Roll(random, 10));
                if (hpDelta < 0) hpDelta = Roll(random, 5) + 5;
                words[RoleWords.Hurt] = Math.Clamp(Word(words[RoleWords.Hurt] - hpEffect / 4), 0, 99);
            }
            else
            {
                var basis = Word(hpEffect + 50 - words[RoleWords.Hurt] / 2 - Roll(random, 10));
                if (basis > 0) basis = -5 - Roll(random, 5);
                hpDelta = Word((basis - 3 * actor[RoleWords.HiddenWeapon]) / 3);
                words[RoleWords.Hurt] = Math.Clamp(Word(words[RoleWords.Hurt] - hpDelta / 10), 0, 99);
            }
            words[RoleWords.Hp] = Math.Clamp(Word(words[RoleWords.Hp] + hpDelta), 0, words[RoleWords.MaximumHp]);
            deltas[0] = Word(words[RoleWords.Hp] - before[RoleWords.Hp]);
        }
        var maximumHp = item[ItemWords.AddMaximumHp];
        if (maximumHp != 0)
        {
            words[RoleWords.MaximumHp] = Math.Clamp(Word(words[RoleWords.MaximumHp] + maximumHp), 0, 999);
            words[RoleWords.Hp] = Math.Min(words[RoleWords.Hp], words[RoleWords.MaximumHp]); deltas[1] = maximumHp;
        }
        var poison = item[ItemWords.AddPoison];
        if (poison != 0)
        {
            int change;
            if (poison > 0)
            {
                change = Word((actor[RoleWords.HiddenWeapon] + poison) / 2 - words[RoleWords.AntiPoison]);
                if (words[RoleWords.AntiPoison] >= 100 || change < 0) change = 0;
                change /= 2;
            }
            else change = Word(poison / 2 + Roll(random, 5) - Roll(random, 5));
            words[RoleWords.Poison] = Math.Clamp(Word(words[RoleWords.Poison] + change), 0, 99);
            deltas[2] = Word(words[RoleWords.Poison] - before[RoleWords.Poison]);
        }
        var physical = item[ItemWords.AddPhysicalPower];
        if (physical != 0)
        {
            words[RoleWords.PhysicalPower] = Math.Clamp(Word(words[RoleWords.PhysicalPower] + physical), 0, 100);
            deltas[3] = Word(words[RoleWords.PhysicalPower] - before[RoleWords.PhysicalPower]);
        }
        if (item[ItemWords.ChangeMpType] == 2) { words[RoleWords.MpType] = 2; deltas[4] = 2; }
        var mp = item[ItemWords.AddMp];
        if (mp != 0)
        {
            words[RoleWords.Mp] = Math.Clamp(Word(words[RoleWords.Mp] + mp), 0, words[RoleWords.MaximumMp]);
            deltas[5] = Word(words[RoleWords.Mp] - before[RoleWords.Mp]);
        }
        var maximumMp = item[ItemWords.AddMaximumMp];
        if (maximumMp != 0)
        {
            words[RoleWords.MaximumMp] = Math.Clamp(Word(words[RoleWords.MaximumMp] + maximumMp), 0, 999);
            words[RoleWords.Mp] = Math.Min(words[RoleWords.Mp], words[RoleWords.MaximumMp]); deltas[6] = maximumMp;
        }
        for (var index = 0; index < 13; index++)
        {
            var delta = item[ItemWords.AddAttack + index];
            words[RoleWords.Attack + index] = Word(words[RoleWords.Attack + index] + delta); deltas[7 + index] = delta;
        }
        // DOS displays these two effects but never writes the corresponding role words.
        deltas[20] = item[ItemWords.AddMorality]; deltas[21] = item[ItemWords.AddAttackTwice];
        deltas[22] = item[ItemWords.AddAttackWithPoison];
        words[RoleWords.AttackWithPoison] = Word(words[RoleWords.AttackWithPoison] + deltas[22]);
        return new(target.Id, before, words, deltas);
    }
    public static void Apply(RoleState target, ItemUseResult result)
    {
        if (target.Id != result.RoleId) throw new ArgumentException("Item effect belongs to another target.");
        // Do not replace unrelated words: consuming a carried item can modify the
        // same actor before committing medicine effects.
        foreach (var (word, value) in result.Changes) target.Set(word, value);
    }
}
