using Godot;

namespace JinYong.Host;

/// <summary>A modal shop view. Its owner validates intents and resumes the event runner.</summary>
public partial class ShopPanel : Control
{
    private Label _balance = null!, _error = null!;
    private VBoxContainer _offers = null!;
    private ScrollContainer _errorScroll = null!;
    private Button _buy = null!, _cancel = null!;
    private readonly Dictionary<int, Button> _rows = [];
    private ShopPanelModel? _model;
    private bool _built, _submitting;
    private int _generation;

    public bool IsOpen { get; private set; }
    public int SelectedSlot { get; private set; } = -1;
    public event Action<ShopActionIntent>? ActionRequested;

    public ShopPanel()
    {
        Name = "Shop";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ZIndex = 110;
    }

    public override void _Ready() => EnsureUi();

    public override void _GuiInput(InputEvent input)
    {
        if (HandleKeyInput(input)) AcceptEvent();
    }

    /// <summary>Consumes modal keys even when a previous exploration editor had focus.</summary>
    public bool HandleKeyInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey key) return false;
        if (!key.Pressed || key.Echo) return true;
        var code = key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;
        switch (code)
        {
            case Key.Up: case Key.W: MoveSelection(-1); break;
            case Key.Down: case Key.S: MoveSelection(1); break;
            case Key.Enter: case Key.KpEnter: RequestBuySelected(); break;
            case Key.Escape: RequestCancel(); break;
        }
        return true;
    }

    public void Render(ShopPanelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureUi();
        var wasOpen = IsOpen;
        var selection = ReferenceEquals(_model?.Token, model.Token) ? SelectedSlot : -1;
        _model = model;
        _generation++;
        var generation = _generation;
        foreach (var child in _offers.GetChildren())
        {
            _offers.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();
        if (model.Offers.Count == 0) _offers.AddChild(new Label { Text = "本店暂时没有可售物品。" });
        _balance.Text = $"{model.CurrencyName}：{model.Balance}";
        foreach (var offer in model.Offers)
        {
            var row = new Button
            {
                Text = $"{offer.Name}    价格 {offer.Price}    库存 {offer.Stock}",
                Alignment = HorizontalAlignment.Left, ToggleMode = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 16)
            };
            row.Pressed += () =>
            {
                if (generation == _generation && IsOpen && !_submitting)
                {
                    Select(offer.Slot);
                    ActionRequested?.Invoke(new(model.Token, ShopActionKind.Select, offer.Slot));
                }
            };
            _rows.Add(offer.Slot, row);
            _offers.AddChild(row);
        }
        SelectedSlot = _rows.ContainsKey(selection) ? selection : model.Offers.FirstOrDefault()?.Slot ?? -1;
        _error.Text = model.Error ?? "";
        _errorScroll.Visible = !string.IsNullOrEmpty(model.Error);
        IsOpen = true;
        SetSubmitting(false);
        Select(SelectedSlot);
        Show();
        if (!wasOpen && IsInsideTree()) GrabFocus();
    }

    public void MoveSelection(int delta)
    {
        if (!IsOpen || _submitting || _model is not { Offers.Count: > 0 } model) return;
        var index = model.Offers.ToList().FindIndex(offer => offer.Slot == SelectedSlot);
        index = (index + delta % model.Offers.Count + model.Offers.Count) % model.Offers.Count;
        Select(model.Offers[index].Slot);
        ActionRequested?.Invoke(new(model.Token, ShopActionKind.Select, SelectedSlot));
    }

    public void RequestBuySelected()
    {
        if (IsOpen && !_submitting && SelectedSlot >= 0 && _model is { } model)
            ActionRequested?.Invoke(new(model.Token, ShopActionKind.Buy, SelectedSlot));
    }

    public void RequestCancel()
    {
        if (IsOpen && !_submitting && _model is { } model)
            ActionRequested?.Invoke(new(model.Token, ShopActionKind.Cancel));
    }

    public void SetSubmitting(bool submitting)
    {
        EnsureUi();
        _submitting = submitting;
        // Insufficient funds still confirms a real DOS purchase attempt and its dialogue.
        _buy.Disabled = submitting || SelectedSlot < 0;
        _cancel.Disabled = submitting;
        foreach (var row in _rows.Values) row.Disabled = submitting;
    }

    public void Close()
    {
        IsOpen = false;
        _model = null;
        _generation++;
        SelectedSlot = -1;
        _submitting = false;
        Hide();
        if (!IsInsideTree()) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus is not null && (ReferenceEquals(focus, this) || IsAncestorOf(focus))) focus.ReleaseFocus();
    }

    private void Select(int slot)
    {
        SelectedSlot = slot;
        foreach (var (id, row) in _rows) row.SetPressedNoSignal(id == slot);
        _buy.Disabled = _submitting || slot < 0;
    }

    private void EnsureUi()
    {
        if (_built) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.82f), MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(veil);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(296, 0) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color("192720"), ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 4, ContentMarginBottom = 4
        };
        style.SetCornerRadiusAll(2);
        card.AddThemeStyleboxOverride("panel", style);
        center.AddChild(card);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        card.AddChild(column);
        var title = new Label { Text = "商店" };
        title.AddThemeFontSizeOverride("font_size", 12);
        column.AddChild(title);
        _balance = new Label();
        column.AddChild(_balance);
        _offers = new VBoxContainer();
        _offers.AddThemeConstantOverride("separation", 2);
        var offersScroll = new ScrollContainer { CustomMinimumSize = new Vector2(280, 88), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(offersScroll); offersScroll.AddChild(_offers);
        _offers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _error = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(280, 0), Modulate = new Color("e6b89d")
        };
        _errorScroll = new ScrollContainer { CustomMinimumSize = new Vector2(280, 16), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(_errorScroll); _errorScroll.AddChild(_error); _error.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _errorScroll.Hide();
        var buttons = new HBoxContainer();
        column.AddChild(buttons);
        _buy = new Button
        {
            Text = "购买一件", FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 16)
        };
        _buy.Pressed += RequestBuySelected;
        buttons.AddChild(_buy);
        _cancel = new Button
        {
            Text = "离开", FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 16)
        };
        _cancel.Pressed += RequestCancel;
        buttons.AddChild(_cancel);
        column.AddChild(new Label { Text = "↑ / ↓ 选择 · Enter 购买 · Esc 离开\n确认购买一件后离店。", Modulate = new Color("94a78f") });
        _built = true;
    }
}
