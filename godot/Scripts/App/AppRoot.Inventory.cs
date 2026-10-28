using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Items;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class AppRoot
{
    private AcceptDialog? _inventoryDialog;
    private ulong? _modalClosedFrame;

    private void ShowInventory()
    {
        if (_world.Session is not { CanSave: true } session || _world.IsMoving || SaveMenuVisible ||
            CurrentPackage?.Story is not { } story || _inventoryDialog is not null) return;
        var dialog = new AcceptDialog { Title = "行囊" };
        dialog.GetOkButton().Text = "返回";
        _inventoryDialog = dialog;
        void CloseInventory()
        {
            if (_inventoryDialog != dialog) return;
            dialog.Hide();
            _modalClosedFrame = Engine.GetProcessFrames(); _inventoryDialog = null;
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            GetWindow().GrabFocus();
            dialog.QueueFree();
        }
        dialog.Confirmed += CloseInventory; dialog.Canceled += CloseInventory;
        var column = new VBoxContainer { Name = "InventoryContents", CustomMinimumSize = new Vector2(520, 0) };
        column.AddThemeConstantOverride("separation", 10);
        var items = new ItemList { Name = "InventoryItems", CustomMinimumSize = new Vector2(520, 210) };
        column.AddChild(items);
        var roles = new OptionButton { Name = "InventoryRole", FocusMode = FocusModeEnum.None };
        foreach (var id in session.Party?.Members.Where(id => id >= 0) ?? []) roles.AddItem(session.Party!.Roles[id].Name, id);
        column.AddChild(roles);
        var roleInfo = new Label { Name = "InventoryRoleInfo", CustomMinimumSize = new Vector2(520, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(roleInfo);
        var use = new Button { Name = "UseItem", Text = "对面前的物件使用", Disabled = true };
        column.AddChild(use);
        var apply = new Button { Name = "ApplyItemToRole", Text = "对所选人物使用", Disabled = true };
        column.AddChild(apply);
        var message = new Label { Name = "InventoryMessage", CustomMinimumSize = new Vector2(520, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(message);
        var entries = Array.Empty<KeyValuePair<int, int>>();
        int? SelectedItem()
        {
            var selected = items.GetSelectedItems();
            return selected.Length > 0 && selected[0] < entries.Length ? entries[selected[0]].Key : null;
        }
        ItemUseMode? Mode(int itemId) => session.BattleResources?.Items.TryGetValue(itemId, out var definition) == true
            ? definition[ItemWords.ItemType] switch { 1 => ItemUseMode.Equip, 2 => ItemUseMode.Practice, 3 => ItemUseMode.Consume, _ => null } : null;
        string ItemName(int id) => id < 0 ? "无" : story.ItemNames.GetValueOrDefault(id, $"物品 {id}");
        void RefreshSelection()
        {
            var item = SelectedItem();
            use.Disabled = item is null || session.IsWorld;
            apply.Disabled = true;
            apply.Text = "对所选人物使用";
            if (roles.Selected < 0 || session.Party is null) { roleInfo.Text = ""; return; }
            var roleId = roles.GetSelectedId(); var person = session.Party.Roles[roleId];
            roleInfo.Text = $"生命 {person[RoleWords.Hp]}/{person[RoleWords.MaximumHp]} · 内力 {person[RoleWords.Mp]}/{person[RoleWords.MaximumMp]}\n" +
                $"装备：{ItemName(person[RoleWords.EquipmentBegin])} / {ItemName(person[RoleWords.EquipmentBegin + 1])}\n修炼：{ItemName(person[RoleWords.PracticeItem])}";
            if (item is not { } itemId || Mode(itemId) is not { } mode) return;
            apply.Text = mode switch { ItemUseMode.Equip => "装备给所选人物", ItemUseMode.Practice => "让所选人物修炼", _ => "让所选人物服用" };
            var target = session.GetItemUseTargets(itemId, mode).FirstOrDefault(target => target.RoleId == roleId);
            apply.Disabled = target?.Eligible != true;
            apply.TooltipText = target?.Reason ?? "";
        }
        void RefreshEntries(int? keepItem = null)
        {
            entries = session.Inventory.OrderBy(pair => pair.Key).ToArray(); items.Clear();
            foreach (var pair in entries) items.AddItem($"{ItemName(pair.Key)}  × {pair.Value}");
            var selected = keepItem is { } id ? Array.FindIndex(entries, entry => entry.Key == id) : -1;
            if (selected >= 0) items.Select(selected);
            RefreshSelection();
        }
        void ApplyToRole(bool confirmTransfer = false, bool confirmCastration = false)
        {
            if (_inventoryDialog != dialog || !session.CanSave || SelectedItem() is not { } item ||
                Mode(item) is not { } mode || roles.Selected < 0) return;
            var role = roles.GetSelectedId();
            var result = mode switch
            {
                ItemUseMode.Equip => session.EquipItem(item, role),
                ItemUseMode.Practice => session.PracticeItem(item, role, confirmTransfer, confirmCastration),
                _ => session.ConsumeItem(item, role)
            };
            if (result.Status is ExplorationItemUseStatus.ConfirmTransfer or ExplorationItemUseStatus.ConfirmCastration)
            {
                var confirmation = new ConfirmationDialog
                {
                    Name = "ConfirmPractice", Title = result.Status == ExplorationItemUseStatus.ConfirmTransfer ? "转移修炼物品" : "修炼条件",
                    DialogText = result.Message
                };
                confirmation.Confirmed += () =>
                {
                    confirmation.QueueFree();
                    ApplyToRole(confirmTransfer || result.Status == ExplorationItemUseStatus.ConfirmTransfer,
                        confirmCastration || result.Status == ExplorationItemUseStatus.ConfirmCastration);
                };
                confirmation.Canceled += () => confirmation.QueueFree();
                dialog.AddChild(confirmation); confirmation.PopupCentered(new Vector2I(440, 180));
                return;
            }
            message.Text = result.Message;
            RefreshEntries(item);
            BindStory(); UpdateSceneInfo(); _world.QueueRedraw();
        }
        use.Pressed += () =>
        {
            if (_inventoryDialog != dialog || SelectedItem() is not { } item) return;
            CloseInventory(); session.UseItem(item); BindStory(); UpdateSceneInfo(); _world.QueueRedraw();
        };
        apply.Pressed += () => ApplyToRole();
        items.ItemSelected += _ => RefreshSelection();
        roles.ItemSelected += _ => RefreshSelection();
        RefreshEntries();
        dialog.AddChild(column); AddChild(dialog); dialog.PopupCentered(new Vector2I(560, 540));
    }
}
