using System.Text;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Formats current role values without mutating state, calculating actions or consuming RNG.</summary>
public static class PartyStatusFormatter
{
    public static string Build(GameSession session, int roleId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Party?.Roles.TryGetValue(roleId, out var role) != true) return "人物资料不可用。";
        string ItemName(int id)
        {
            if (id < 0) return "无";
            if (session.Story?.ItemNames.TryGetValue(id, out var name) == true) return name;
            return session.BattleResources?.Items.TryGetValue(id, out var item) == true ? item.Name : $"物品 {id}";
        }
        string MagicName(int id) => session.BattleResources?.Magics.TryGetValue(id, out var magic) == true
            ? magic.Name : $"武功 {id}";
        var type = role[RoleWords.MpType] switch { 0 => "阴性", 1 => "阳性", 2 => "阴阳调和", _ => "未知" };
        var text = new StringBuilder();
        text.AppendLine(string.IsNullOrWhiteSpace(role.Definition.Nickname) ? role.Name : $"{role.Name} · {role.Definition.Nickname}");
        text.AppendLine($"等级 {role[RoleWords.Level]}    经验 {role[RoleWords.Experience]}");
        text.AppendLine($"生命 {role[RoleWords.Hp]} / {role[RoleWords.MaximumHp]}    内力 {role[RoleWords.Mp]} / {role[RoleWords.MaximumMp]}（{type}）");
        text.AppendLine($"体力 {role[RoleWords.PhysicalPower]}    伤势 {role[RoleWords.Hurt]}    中毒 {role[RoleWords.Poison]}");
        text.AppendLine();
        text.AppendLine($"攻击 {role[RoleWords.Attack]}    防御 {role[RoleWords.Defence]}    轻功 {role[RoleWords.Speed]}");
        text.AppendLine($"医疗 {role[RoleWords.Medicine]}    用毒 {role[RoleWords.UsePoison]}    解毒 {role[RoleWords.Detoxification]}    抗毒 {role[RoleWords.AntiPoison]}");
        text.AppendLine($"拳掌 {role[RoleWords.Fist]}    御剑 {role[RoleWords.Sword]}    耍刀 {role[RoleWords.Knife]}    特殊兵器 {role[RoleWords.Unusual]}");
        text.AppendLine($"暗器 {role[RoleWords.HiddenWeapon]}    武学常识 {role[RoleWords.Knowledge]}    攻击带毒 {role[RoleWords.AttackWithPoison]}");
        text.AppendLine($"资质 {role[RoleWords.Iq]}    品德 {role[RoleWords.Morality]}    声望 {role[RoleWords.Fame]}");
        text.AppendLine();
        text.AppendLine($"武器：{ItemName(role[RoleWords.EquipmentBegin])}");
        text.AppendLine($"防具：{ItemName(role[RoleWords.EquipmentBegin + 1])}");
        text.AppendLine($"修炼：{ItemName(role[RoleWords.PracticeItem])}    修炼经验 {role[RoleWords.ItemExperience]}");
        text.AppendLine($"制作经验 {role[RoleWords.MakeItemExperience]}");
        text.AppendLine();
        text.AppendLine("已学武功");
        var learned = false;
        for (var slot = 0; slot < RoleWords.MagicCount; slot++)
        {
            var id = role[RoleWords.MagicIdBegin + slot];
            if (id <= 0) continue;
            learned = true;
            text.AppendLine($"{MagicName(id)}    熟练 {role[RoleWords.MagicLevelBegin + slot]}");
        }
        if (!learned) text.AppendLine("尚未习得武功。");
        return text.ToString().TrimEnd();
    }
}
