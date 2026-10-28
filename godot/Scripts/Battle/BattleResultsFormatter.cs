using JinYong.Core.Battles;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Formats frozen settlement values; session data supplies names only.</summary>
public static class BattleResultsFormatter
{
    private static readonly (int Attribute, string Name)[] GrowthAttributes =
    [
        (RoleWords.MaximumHp, "最大生命"), (RoleWords.MaximumMp, "最大内力"),
        (RoleWords.Attack, "攻击"), (RoleWords.Speed, "轻功"), (RoleWords.Defence, "防御"),
        (RoleWords.Medicine, "医疗"), (RoleWords.UsePoison, "用毒"),
        (RoleWords.Detoxification, "解毒"), (RoleWords.AntiPoison, "抗毒"),
        (RoleWords.Fist, "拳掌"), (RoleWords.Sword, "御剑"), (RoleWords.Knife, "耍刀"),
        (RoleWords.Unusual, "特殊兵器"), (RoleWords.HiddenWeapon, "暗器"),
        (RoleWords.Knowledge, "武学常识"), (RoleWords.Morality, "道德"),
        (RoleWords.AttackWithPoison, "攻击带毒")
    ];

    public static BattleResultsModel BuildModel(GameSession session, BattleSettlementResult result,
        string battleName, bool victory, bool hasLivingFriendly)
    {
        ArgumentNullException.ThrowIfNull(battleName);
        var sections = Build(session, result);
        var summary = battleName + (victory && hasLivingFriendly
            ? $"\n存活队员每人分得经验 {result.SharedExperience}。" : "\n休整之后，继续闯荡江湖。");
        return new(victory ? "战斗胜利" : "战斗落败", summary, sections);
    }

    public static IReadOnlyList<BattleResultSection> Build(GameSession session, BattleSettlementResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        var sections = new List<BattleResultSection>();
        foreach (var role in result.Roles.Where(role => role.Side == 0))
        {
            var lines = new List<string>();
            if (role.ExperienceMessageRequired)
                lines.Add($"获得经验 {role.ExperienceGained}");
            if (role.OldLevel != role.NewLevel)
                lines.Add($"等级 {role.OldLevel} → {role.NewLevel}");

            var hasSnapshots = role.BeforeWords.Count == RoleWords.Count && role.AfterWords.Count == RoleWords.Count;
            if (hasSnapshots)
            {
                foreach (var (attribute, name) in GrowthAttributes)
                {
                    var before = role.BeforeWords[attribute];
                    var after = role.AfterWords[attribute];
                    if (before != after)
                        lines.Add($"{name} {before} → {after}（{Signed((long)after - before)}）");
                }
                if (role.BeforeWords[RoleWords.MpType] != role.AfterWords[RoleWords.MpType])
                    lines.Add(role.AfterWords[RoleWords.MpType] == 2 ? "内力达到阴阳调和" : "内力性质发生变化");
                if (role.BeforeWords[RoleWords.AttackTwice] != role.AfterWords[RoleWords.AttackTwice])
                    lines.Add(role.AfterWords[RoleWords.AttackTwice] == 1 ? "学会左右互搏" : "左右互搏能力发生变化");
            }

            if (role.Practiced)
                lines.Add(role.PracticeItemId >= 0 ? $"修炼《{ItemName(session, role.PracticeItemId)}》完成" : "修炼完成");
            else if (role.ExperienceMessageRequired && role.PracticeItemId >= 0 && hasSnapshots &&
                role.BeforeWords[RoleWords.ItemExperience] != role.AfterWords[RoleWords.ItemExperience])
                lines.Add($"《{ItemName(session, role.PracticeItemId)}》修炼经验 " +
                    $"{role.BeforeWords[RoleWords.ItemExperience]} → {role.AfterWords[RoleWords.ItemExperience]}");

            // A newly learned art may retain proficiency zero; the ID change is
            // meaningful independently of the proficiency difference.
            foreach (var magic in role.MagicChanges.Where(magic => magic.NewMagicId > 0))
            {
                var name = MagicName(session, magic.NewMagicId);
                if (magic.OldMagicId <= 0 || magic.OldMagicId != magic.NewMagicId)
                    lines.Add($"新学会《{name}》（熟练度 {magic.NewProficiency}）");
                else if (magic.OldProficiency != magic.NewProficiency)
                    lines.Add($"《{name}》熟练度 {magic.OldProficiency} → {magic.NewProficiency}");
            }
            if (role.CraftedItemId is { } item && role.CraftedCount > 0)
                lines.Add($"制成 {ItemName(session, item)} × {role.CraftedCount}");

            if (lines.Count > 0)
                sections.Add(new(RoleName(session, role.RoleId), lines.AsReadOnly()));
        }

        // Inventory changes are whole-settlement net differences. Do not assign
        // a shared material cost to a particular crafter or multiply it per role.
        var inventoryLines = new List<string>();
        foreach (var item in result.InventoryChanges)
        {
            var difference = (long)item.NewCount - item.OldCount;
            if (difference == 0) continue;
            var action = difference > 0 ? "成品净增加" : "材料净消耗";
            inventoryLines.Add($"{action}：{ItemName(session, item.ItemId)} × {Math.Abs(difference)}" +
                $"（{item.OldCount} → {item.NewCount}）");
        }
        if (inventoryLines.Count > 0)
            sections.Add(new("制作后的行囊变化", inventoryLines.AsReadOnly()));
        return sections.AsReadOnly();
    }

    private static string Signed(long value) => value > 0 ? $"+{value}" : value.ToString();

    private static string RoleName(GameSession session, int id) =>
        session.Party?.Roles.TryGetValue(id, out var role) == true && !string.IsNullOrWhiteSpace(role.Name)
            ? role.Name : $"人物 {id}";

    private static string ItemName(GameSession session, int id) =>
        session.BattleResources?.Items.TryGetValue(id, out var item) == true && !string.IsNullOrWhiteSpace(item.Name)
            ? item.Name : session.Story?.ItemNames.GetValueOrDefault(id) ?? $"物品 {id}";

    private static string MagicName(GameSession session, int id) =>
        session.BattleResources?.Magics.TryGetValue(id, out var magic) == true && !string.IsNullOrWhiteSpace(magic.Name)
            ? magic.Name : $"武功 {id}";
}
