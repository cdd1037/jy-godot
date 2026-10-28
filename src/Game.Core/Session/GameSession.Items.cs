using JinYong.Core.Battles;
using JinYong.Core.Items;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    public IReadOnlyList<ItemUseTarget> GetItemUseTargets(int itemId, ItemUseMode mode)
    {
        if (Party is null) return [];
        var unavailable = ValidateExplorationItem(itemId,out var item);
        return Party.Members.Where(id => id >= 0).Select(id =>
        {
            var role = Party.Roles[id];
            var eligibility = unavailable is null ? ItemEligibilityRules.Check(role,item!,mode) : new ItemEligibility(false,unavailable);
            return new ItemUseTarget(id,role.Name,eligibility.Eligible,eligibility.Reason);
        }).ToArray();
    }

    public ExplorationItemUseResult EquipItem(int itemId, int roleId)
    {
        var invalid = ValidateItemTarget(itemId,roleId,ItemUseMode.Equip,out var item);
        if (invalid is not null) return Rejected(invalid);
        try { Party!.AssignEquipment(roleId,item!,BattleResources!.Items); }
        catch (InvalidOperationException error) { return Rejected(error.Message); }
        PresentationRevision++;
        return new(ExplorationItemUseStatus.Applied,$"{Party!.Roles[roleId].Name} 装备了 {item!.Name}。");
    }

    public ExplorationItemUseResult PracticeItem(int itemId, int roleId, bool confirmTransfer = false, bool confirmCastration = false)
    {
        var unavailable = ValidateExplorationItem(itemId,out var item);
        if (unavailable is not null) return Rejected(unavailable);
        if (Party?.Contains(roleId) != true) return Rejected("请选择当前队伍中的人物。");
        if (item![ItemWords.ItemType] != 2) return Rejected("此物品不能用于修炼。");
        // The original asks before selecting any replacement, including the same user.
        if (Party.ItemUser(itemId) >= 0 && !confirmTransfer)
            return new(ExplorationItemUseStatus.ConfirmTransfer,"此物品已有人修炼，是否重新指定修炼者？");
        var role = Party.Roles[roleId];
        var eligibility = ItemEligibilityRules.Check(role,item,ItemUseMode.Practice);
        if (!eligibility.Eligible) return Rejected(eligibility.Reason!);
        var castrate = itemId is 78 or 93 && unchecked((short)role[RoleWords.Sex]) == 0;
        if (castrate && !confirmCastration)
            return new(ExplorationItemUseStatus.ConfirmCastration,"修炼此功需要挥剑自宫，是否继续？");
        bool changed;
        try { changed = Party.AssignPractice(roleId,item,BattleResources!.Items,castrate); }
        catch (InvalidOperationException error) { return Rejected(error.Message); }
        if (!changed) return new(ExplorationItemUseStatus.Unchanged,"此人已在修炼该物品，原有修炼经验保留。");
        PresentationRevision++;
        return new(ExplorationItemUseStatus.Applied,$"{role.Name} 开始修炼 {item.Name}。");
    }

    public ExplorationItemUseResult ConsumeItem(int itemId, int roleId)
    {
        var invalid = ValidateItemTarget(itemId,roleId,ItemUseMode.Consume,out var item);
        if (invalid is not null) return Rejected(invalid);
        var role = Party!.Roles[roleId];
        try
        {
            var effect = ItemUseRules.Calculate(role,role,item!,Events!.DrawRandom);
            if (!effect.HasEffect) return new(ExplorationItemUseStatus.Unchanged,"此物品没有产生效果，未消耗物品。");
            if (!TryConsumeInventoryItem(itemId,1)) return Rejected("行囊中的物品数量不足。");
            ItemUseRules.Apply(role,effect);
            PresentationRevision++;
            return new(ExplorationItemUseStatus.Applied,$"{role.Name} 服用了 {item!.Name}。");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return Rejected("无法服用："+error.Message); }
    }

    private string? ValidateExplorationItem(int itemId, out ItemDefinition? item)
    {
        item = null;
        if (!CanSave) return "请等待行动或剧情结束后再使用物品。";
        if (Party is null || Events is null || BattleResources is null) return "当前会话没有完整的物品和队伍资料。";
        if (!BattleResources.Items.TryGetValue(itemId,out item)) return "物品定义不存在。";
        if (_inventory.GetValueOrDefault(itemId) <= 0) return "行囊中没有这件物品。";
        if (item[ItemWords.ShowIntroduction] != 1) return "此物品不能直接使用。";
        return null;
    }

    private string? ValidateItemTarget(int itemId, int roleId, ItemUseMode mode, out ItemDefinition? item)
    {
        var unavailable = ValidateExplorationItem(itemId,out item);
        if (unavailable is not null) return unavailable;
        if (Party?.Contains(roleId) != true) return "请选择当前队伍中的人物。";
        var eligibility = ItemEligibilityRules.Check(Party.Roles[roleId],item!,mode);
        return eligibility.Eligible ? null : eligibility.Reason;
    }

    private static ExplorationItemUseResult Rejected(string message) => new(ExplorationItemUseStatus.Rejected,message);
}
