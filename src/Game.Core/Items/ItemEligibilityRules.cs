using JinYong.Core.Battles;
using JinYong.Core.Session;

namespace JinYong.Core.Items;

public enum ItemUseMode { Equip, Practice, Consume }
public enum ExplorationItemUseStatus { Applied, Unchanged, Rejected, ConfirmTransfer, ConfirmCastration }
public sealed record ItemUseTarget(int RoleId, string Name, bool Eligible, string? Reason);
public sealed record ExplorationItemUseResult(ExplorationItemUseStatus Status, string Message)
{
    public bool Success => Status is ExplorationItemUseStatus.Applied or ExplorationItemUseStatus.Unchanged;
}
public sealed record ItemEligibility(bool Eligible, string? Reason);

/// <summary>
/// DOS baseline: OpenLegend research/evidence/functions/Z_DAT/0x2BD8B.md and
/// 0x2A86C.md, not later NG+ thresholds. Requirements read signed low words;
/// a known practice magic overrides every preceding suitability failure.
/// </summary>
public static class ItemEligibilityRules
{
    private static int S(int value) => unchecked((short)value);
    public static bool KnowsMagic(RoleState role, int magicId) => Enumerable.Range(0,RoleWords.MagicCount)
        .Any(slot => S(role[RoleWords.MagicIdBegin+slot]) == magicId);

    public static ItemEligibility Check(RoleState role, ItemDefinition item, ItemUseMode mode)
    {
        if (!Enum.IsDefined(mode)) return new(false,"未知使用方式。");
        var type = mode switch { ItemUseMode.Equip => 1, ItemUseMode.Practice => 2, _ => 3 };
        if (item[ItemWords.ItemType] != type) return new(false,"此物品不支持这种使用方式。");
        if (mode == ItemUseMode.Consume) return new(true,null); // The medicine caller has no equipment/practice gate.
        if (mode == ItemUseMode.Practice && item[ItemWords.MagicId] != -1 &&
            S(role[RoleWords.MagicIdBegin+RoleWords.MagicCount-1]) > 0 && !KnowsMagic(role,item[ItemWords.MagicId]))
            return new(false,"已经学满十种武功，无法修炼新的武功。");
        return MeetsRequirements(role,item) ? new(true,null) : new(false,
            mode == ItemUseMode.Equip ? "此人不适合配备此物品。" : "此人不适合修炼此物品。");
    }

    public static bool MeetsRequirements(RoleState role, ItemDefinition item)
    {
        var type = S(role[RoleWords.MpType]); var neededType = item[ItemWords.NeedMpType];
        var suitable = type == 2 || neededType == 2 || type == neededType;
        if (item[ItemWords.OnlySuitableRole] != -1 && item[ItemWords.OnlySuitableRole] != role.Id) suitable = false;
        (int Role,int Item)[] requirements =
        [
            (RoleWords.Mp,ItemWords.NeedMp),(RoleWords.Attack,ItemWords.NeedAttack),(RoleWords.Speed,ItemWords.NeedSpeed),
            (RoleWords.UsePoison,ItemWords.NeedUsePoison),(RoleWords.Medicine,ItemWords.NeedMedicine),
            (RoleWords.Detoxification,ItemWords.NeedDetoxification),(RoleWords.Fist,ItemWords.NeedFist),
            (RoleWords.Sword,ItemWords.NeedSword),(RoleWords.Knife,ItemWords.NeedKnife),
            (RoleWords.Unusual,ItemWords.NeedUnusual),(RoleWords.HiddenWeapon,ItemWords.NeedHiddenWeapon)
        ];
        foreach (var (word,needed) in requirements)
            if (S(role[word]) < item[needed]) suitable = false;
        var iq = S(role[RoleWords.Iq]); var neededIq = item[ItemWords.NeedIq];
        if (neededIq >= 0 ? iq < neededIq : iq > -neededIq) suitable = false;
        if (item[ItemWords.ItemType] == 2)
        {
            if (item.Id is 78 or 93 && S(role[RoleWords.Sex]) == 1) suitable = false;
            if (KnowsMagic(role,item[ItemWords.MagicId])) suitable = true;
        }
        return suitable;
    }
}
