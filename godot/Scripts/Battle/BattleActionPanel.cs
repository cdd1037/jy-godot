using Godot;

namespace JinYong.Host;

/// <summary>Renders supplied display data and emits explicit UI intents.</summary>
public partial class BattleActionPanel : VBoxContainer
{
    private Label? _statusLabel, _targetHintLabel;
    private long _renderVersion;
    private int _selectedMagicSlot = -1, _selectedItemId = -1;
    public event Action<BattleActionIntent>? ActionRequested;

    public override void _Ready() => AddThemeConstantOverride("separation", 9);

    public void Render(BattleActionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var version = ++_renderVersion;
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _targetHintLabel = null;
        _statusLabel = new Label { Text = model.Status, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_statusLabel);
        void Emit(BattleActionKind kind, int value = -1, IReadOnlyList<int>? roles = null)
        {
            // Removed controls can still have queued input while QueueFree waits.
            // This is a UI-generation check, not a game-state validation.
            if (version != _renderVersion) return;
            ActionRequested?.Invoke(new(model.Token, kind, value, roles));
        }
        Button ActionButton(string text, BattleActionKind kind, Func<int>? value = null,
            Func<IReadOnlyList<int>>? selectedRoles = null)
        {
            var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new(0, 34) };
            button.Pressed += () =>
            {
                if (version != _renderVersion) return;
                Emit(kind, value?.Invoke() ?? -1, selectedRoles?.Invoke());
            };
            AddChild(button); return button;
        }

        if (model.Present)
        {
            if (model.WaitForConfirmation) ActionButton("继续", BattleActionKind.ConfirmPresentation);
            else AddChild(new Label { Text = "正在行动……" });
            return;
        }
        if (model.IsSelecting)
        {
            var options = new List<(int Id, CheckBox Box)>();
            foreach (var option in model.PartyOptions)
            {
                var box = new CheckBox { Text = option.Name + (option.Mandatory ? "（必选）" : ""),
                    ButtonPressed = true, Disabled = option.Mandatory, FocusMode = FocusModeEnum.None };
                options.Add((option.Id, box)); AddChild(box);
            }
            ActionButton("确认出战", BattleActionKind.ConfirmParty,
                selectedRoles: () => Array.AsReadOnly(options.Where(option => option.Box.ButtonPressed).Select(option => option.Id).ToArray()));
            return;
        }
        if (model.Finished)
        {
            ActionButton("返回场景，继续剧情", BattleActionKind.ShowResults);
            return;
        }
        if (!model.IsFriendlyTurn)
        {
            AddChild(new Label { Text = "敌方行动中……" }); return;
        }

        ActionButton("移动：点击可到达的格子", BattleActionKind.SelectMove).Disabled = !model.CanMove;
        var magic = new OptionButton { Name = "BattleMagic", FocusMode = FocusModeEnum.None };
        foreach (var option in model.MagicOptions) magic.AddItem(option.Name, option.Slot);
        _selectedMagicSlot = model.MagicOptions.Any(option => option.Slot == model.SelectedMagicSlot)
            ? model.SelectedMagicSlot : model.MagicOptions.FirstOrDefault()?.Slot ?? -1;
        for (var index = 0; index < model.MagicOptions.Count; index++)
            if (model.MagicOptions[index].Slot == _selectedMagicSlot) magic.Select(index);
        magic.ItemSelected += index =>
        {
            if (version != _renderVersion) return;
            _selectedMagicSlot = magic.GetItemId((int)index); Emit(BattleActionKind.SelectMagic, _selectedMagicSlot);
        };
        AddChild(magic);
        ActionButton("攻击：点击目标格子", BattleActionKind.SelectAttack, () => _selectedMagicSlot).Disabled = model.MagicOptions.Count == 0;
        ActionButton("用毒：点击敌人", BattleActionKind.SelectPoison).Disabled = !model.CanPoison;
        ActionButton("解毒：点击队友或自己", BattleActionKind.SelectDetox).Disabled = !model.CanDetox;
        ActionButton("医疗：点击队友或自己", BattleActionKind.SelectMedicine).Disabled = !model.CanMedicine;

        var items = new OptionButton { Name = "BattleItem", FocusMode = FocusModeEnum.None };
        foreach (var option in model.ItemOptions) items.AddItem(option.Name, option.Id);
        _selectedItemId = model.ItemOptions.Any(option => option.Id == model.SelectedItemId)
            ? model.SelectedItemId : model.ItemOptions.FirstOrDefault()?.Id ?? -1;
        for (var index = 0; index < model.ItemOptions.Count; index++)
            if (model.ItemOptions[index].Id == _selectedItemId) items.Select(index);
        items.ItemSelected += index =>
        {
            if (version != _renderVersion) return;
            _selectedItemId = items.GetItemId((int)index); Emit(BattleActionKind.SelectItem, _selectedItemId);
        };
        AddChild(items);
        var use = ActionButton("使用所选物品", BattleActionKind.UseItem, () => _selectedItemId);
        use.Name = "UseBattleItem"; use.Disabled = model.ItemOptions.Count == 0;
        ActionButton("休息", BattleActionKind.Rest); ActionButton("等待", BattleActionKind.Wait);
        _targetHintLabel = new Label { Text = model.TargetHint, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_targetHintLabel);
    }
    public void UpdateStatus(string status)
    {
        if (_statusLabel is not null) _statusLabel.Text = status;
    }
    public void UpdateTargetHint(string hint)
    {
        if (_targetHintLabel is not null) _targetHintLabel.Text = hint;
    }
}
