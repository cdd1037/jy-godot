using Godot;

namespace JinYong.Host;

/// <summary>A game-layer inventory view. Only renders supplied values and emits typed intents.</summary>
public partial class InventoryPanel : Control
{
    private PanelContainer _card = null!;
    private Control? _confirmationLayer;
    private InventoryModel? _model;
    private bool _built;
    private long _renderVersion;
    private ItemList _items = null!;
    private OptionButton _roles = null!;
    private Button _apply = null!, _use = null!, _return = null!;
    private Label _message = null!;
    private Button? _confirm, _cancelConfirm;

    public event Action<InventoryIntent>? IntentRequested;
    public event Action<InputEvent>? KeyInput;
    public bool IsOpen => _model is not null && Visible;
    public bool ConfirmationOpen => IsOpen && _model?.Confirmation is not null;
    internal ItemList Items => _items;
    internal OptionButton Roles => _roles;
    internal Button ApplyButton => _apply;
    internal Button UseButton => _use;
    internal Button ReturnButton => _return;
    internal Label MessageLabel => _message;
    internal Button? ConfirmButton => _confirm;
    internal Button? CancelConfirmButton => _cancelConfirm;

    public InventoryPanel()
    {
        Name = "InventoryMenu"; Visible = false; MouseFilter = MouseFilterEnum.Stop; ZIndex = 110;
        FocusMode = FocusModeEnum.All;
    }

    public override void _Ready() => EnsureUi();

    public override void _GuiInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey) return;
        KeyInput?.Invoke(input);
        AcceptEvent();
    }

    public void Render(InventoryModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureUi();
        var version = ++_renderVersion;
        _model = model;
        foreach (var child in _card.GetChildren()) { _card.RemoveChild(child); child.QueueFree(); }
        if (_confirmationLayer is { } oldConfirmation)
        { RemoveChild(oldConfirmation); oldConfirmation.QueueFree(); _confirmationLayer = null; }
        _confirm = _cancelConfirm = null;
        var confirming = model.Confirmation is not null;
        void Emit(InventoryActionKind kind, int value = -1)
        {
            if (_model is not null && version == _renderVersion)
                IntentRequested?.Invoke(new(model.Context, kind, value));
        }

        var column = new VBoxContainer { Name = "InventoryContents" };
        column.AddThemeConstantOverride("separation", 3); _card.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        var title = new Label { Text = "行囊", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 12); header.AddChild(title);
        _return = new Button { Name = "ReturnInventory", Text = "返回（Esc）", Disabled = confirming, FocusMode = FocusModeEnum.None };
        _return.Pressed += () => Emit(InventoryActionKind.Close); header.AddChild(_return);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6); column.AddChild(body);
        _items = new ItemList
        {
            Name = "InventoryItems", CustomMinimumSize = new Vector2(126, 116),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None, MouseFilter = confirming ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop
        };
        foreach (var item in model.Items) _items.AddItem($"{item.Name}  × {item.Count}");
        var selectedItem = model.Items.ToList().FindIndex(item => item.Id == model.SelectedItemId);
        if (selectedItem >= 0) { _items.Select(selectedItem); _items.EnsureCurrentIsVisible(); }
        _items.ItemSelected += index =>
        {
            if (index >= 0 && index < model.Items.Count) Emit(InventoryActionKind.SelectItem, model.Items[(int)index].Id);
        };
        body.AddChild(_items);

        var details = new VBoxContainer { CustomMinimumSize = new Vector2(148, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 3);
        var detailsScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(154, 116), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(detailsScroll); detailsScroll.AddChild(details);
        details.AddChild(Wrapped("InventoryItemInfo", model.ItemDetails));
        details.AddChild(new HSeparator());
        details.AddChild(new Label { Text = "使用人物" });
        _roles = new OptionButton { Name = "InventoryRole", Disabled = confirming || model.Roles.Count == 0, FocusMode = FocusModeEnum.None };
        foreach (var role in model.Roles) _roles.AddItem(role.Name, role.Id);
        var selectedRole = model.Roles.ToList().FindIndex(role => role.Id == model.SelectedRoleId);
        if (selectedRole >= 0) _roles.Select(selectedRole);
        _roles.ItemSelected += index =>
        {
            if (index >= 0 && index < model.Roles.Count) Emit(InventoryActionKind.SelectRole, model.Roles[(int)index].Id);
        };
        details.AddChild(_roles);
        details.AddChild(Wrapped("InventoryRoleInfo", model.RoleDetails));
        _apply = new Button
        {
            Name = "ApplyItemToRole", Text = model.ApplyText, Disabled = confirming || !model.CanApply,
            TooltipText = model.ApplyReason, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 16)
        };
        _apply.Pressed += () => Emit(InventoryActionKind.ApplyToRole); details.AddChild(_apply);
        _use = new Button
        {
            Name = "UseItem", Text = "对面前的物件使用（E）", Disabled = confirming || !model.CanUseOnScene,
            FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 16)
        };
        _use.Pressed += () => Emit(InventoryActionKind.UseOnScene); details.AddChild(_use);
        if (!model.CanApply && !string.IsNullOrWhiteSpace(model.ApplyReason)) details.AddChild(Wrapped("InventoryEligibility", model.ApplyReason));
        _message = Wrapped("InventoryMessage", model.Message);
        var messageScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 18), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(messageScroll); messageScroll.AddChild(_message);
        _message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddChild(new Label { Text = "↑↓ 物品 · ←→ 人物 · Enter 使用 · E 交互", Modulate = new Color("a5b8a4") });
        if (model.Confirmation is { } confirmation) BuildConfirmation(confirmation, Emit);
        Show();
        if (IsInsideTree()) GrabFocus();
    }

    public void Close()
    {
        _renderVersion++; _model = null; Hide();
        if (!IsInsideTree()) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus is not null && (ReferenceEquals(focus, this) || IsAncestorOf(focus))) focus.ReleaseFocus();
    }

    private void BuildConfirmation(InventoryConfirmationModel model, Action<InventoryActionKind, int> emit)
    {
        var layer = new Control { Name = "PracticeConfirmation", MouseFilter = MouseFilterEnum.Stop, ZIndex = 1 };
        layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(layer); _confirmationLayer = layer;
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.78f), MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); layer.AddChild(veil);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); layer.AddChild(center);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(260, 0) };
        card.AddThemeStyleboxOverride("panel", CardStyle()); center.AddChild(card);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 4); card.AddChild(column);
        var title = new Label { Text = model.Title }; title.AddThemeFontSizeOverride("font_size", 11); column.AddChild(title);
        var confirmationScroll = new ScrollContainer { CustomMinimumSize = new Vector2(248, 42), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(confirmationScroll);
        var confirmationText = Wrapped("PracticeConfirmationMessage", model.Message);
        confirmationText.SizeFlagsHorizontal = SizeFlags.ExpandFill; confirmationScroll.AddChild(confirmationText);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 3); column.AddChild(row);
        _cancelConfirm = new Button { Name = "CancelPractice", Text = "取消", SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        _cancelConfirm.Pressed += () => emit(InventoryActionKind.CancelPractice, -1); row.AddChild(_cancelConfirm);
        _confirm = new Button { Name = "ConfirmPractice", Text = "确认", SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        _confirm.Pressed += () => emit(InventoryActionKind.ConfirmPractice, -1); row.AddChild(_confirm);
        column.AddChild(new Label { Text = "Enter / 空格确认 · Esc 取消", Modulate = new Color("a5b8a4") });
    }

    private void EnsureUi()
    {
        if (_built) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.86f), MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(veil);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 4);
        AddChild(margin);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; margin.AddChild(center);
        _card = new PanelContainer { CustomMinimumSize = new Vector2(304, 190) };
        _card.AddThemeStyleboxOverride("panel", CardStyle()); center.AddChild(_card);
        _built = true;
    }

    private static Label Wrapped(string name, string text) => new()
    {
        Name = name, Text = text, CustomMinimumSize = new Vector2(0, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart
    };

    private static StyleBoxFlat CardStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color("192720"), ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 4, ContentMarginBottom = 4
        };
        style.SetCornerRadiusAll(2); return style;
    }
}
