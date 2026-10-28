using JinYong.Core.Battles;

namespace JinYong.Core.Session;

public sealed partial class PartyState
{
    internal int ItemUser(int itemId) => _itemUsers.GetValueOrDefault(itemId,-1);

    private void ValidateBinding(int roleId, ItemDefinition item, int field, IReadOnlyDictionary<int,ItemDefinition> items)
    {
        _ = Role(roleId);
        var user = ItemUser(item.Id);
        if (user < -1 || user >= 0 && !_roles.ContainsKey(user)) throw new InvalidOperationException("物品占用者不存在。");
        var previous = Role(roleId)[field];
        if (previous < -1 || previous >= 0 && !items.ContainsKey(previous)) throw new InvalidOperationException("人物原有物品引用不存在。");
    }

    // Ownership lives in this existing PartyState; item definitions remain immutable.
    // 0x2A86C: all nonzero equipment-type words address the second slot.
    internal void AssignEquipment(int roleId, ItemDefinition item, IReadOnlyDictionary<int,ItemDefinition> items)
    {
        var field = RoleWords.EquipmentBegin + (item[ItemWords.EquipmentType] == 0 ? 0 : 1);
        ValidateBinding(roleId,item,field,items);
        var role = Role(roleId); var previousUser = ItemUser(item.Id);
        if (previousUser >= 0) Role(previousUser).Set(field,-1);
        var previousItem = role[field];
        if (previousItem >= 0) _itemUsers[previousItem] = -1;
        role.Set(field,item.Id); _itemUsers[item.Id] = roleId;
    }

    internal bool AssignPractice(int roleId, ItemDefinition item, IReadOnlyDictionary<int,ItemDefinition> items, bool castrate)
    {
        ValidateBinding(roleId,item,RoleWords.PracticeItem,items);
        var role = Role(roleId); var previousUser = ItemUser(item.Id);
        var changedSex = castrate && role[RoleWords.Sex] != 2;
        if (castrate) role.Set(RoleWords.Sex,2);
        // Same-user selection does not reset either experience counter.
        if (previousUser == roleId) return changedSex;
        if (previousUser >= 0)
        {
            Role(previousUser).Set(RoleWords.PracticeItem,-1);
            Role(previousUser).Set(RoleWords.ItemExperience,0);
        }
        var previousItem = role[RoleWords.PracticeItem];
        if (previousItem >= 0) _itemUsers[previousItem] = -1;
        _itemUsers[item.Id] = roleId; role.Set(RoleWords.PracticeItem,item.Id);
        role.Set(RoleWords.ItemExperience,0); role.Set(RoleWords.MakeItemExperience,0);
        return true;
    }
}
