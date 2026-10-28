using Godot;

namespace JinYong.Host;

/// <summary>A model-only modal page, shared by title, game, selection and information menus.</summary>
public partial class GameMenuPanel : Control
{
    private Label _title = null!, _subtitle = null!, _body = null!, _hint = null!, _error = null!;
    private VBoxContainer _entries = null!;
    private ScrollContainer _scroll = null!;
    private Button _back = null!;
    private readonly List<Button> _rows = [];
    private GameMenuModel? _model;
    private bool _built;
    private int _generation;
    private ulong? _openedFrame, _lastActionFrame;

    public bool IsOpen { get; private set; }
    public int SelectedIndex { get; private set; } = -1;
    public event Action<string>? EntryActivated;
    public event Action? BackRequested;

    public GameMenuPanel()
    {
        Name = "GameMenu";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ZIndex = 120;
    }

    public override void _Ready() => EnsureUi();

    public override void _GuiInput(InputEvent input)
    {
        if (HandleKeyInput(input)) AcceptEvent();
    }

    public void Render(GameMenuModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(model.Entries);
        EnsureUi();
        var wasOpen = IsOpen;
        var samePage = _model?.Title == model.Title;
        var previousId = samePage && SelectedIndex >= 0 && SelectedIndex < (_model?.Entries.Count ?? 0)
            ? _model!.Entries[SelectedIndex].Id : null;
        // Freeze the page's list so a caller cannot change a queued button's meaning.
        _model = model with { Entries = Array.AsReadOnly(model.Entries.ToArray()) };
        _generation++;
        var generation = _generation;
        foreach (var child in _entries.GetChildren())
        {
            _entries.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();
        _title.Text = model.Title;
        _subtitle.Text = model.Subtitle;
        _subtitle.Visible = !string.IsNullOrEmpty(model.Subtitle);
        _body.Text = model.Body;
        _body.Visible = !string.IsNullOrEmpty(model.Body);
        _error.Text = model.Error ?? "";
        _error.Visible = !string.IsNullOrEmpty(model.Error);
        foreach (var (entry, index) in _model.Entries.Select((entry, index) => (entry, index)))
        {
            var row = new Button
            {
                Name = $"MenuEntry{index}", Text = entry.Text, Disabled = !entry.Enabled,
                Alignment = HorizontalAlignment.Left, ToggleMode = true,
                FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(660, 42),
                TooltipText = entry.Hint ?? ""
            };
            row.AddThemeFontSizeOverride("font_size", 22);
            row.Pressed += () =>
            {
                if (!IsOpen || generation != _generation) return;
                Select(index);
                Activate(index, generation);
            };
            row.MouseEntered += () =>
            {
                if (IsOpen && generation == _generation && entry.Enabled) Select(index);
            };
            _rows.Add(row);
            _entries.AddChild(row);
        }
        var previousIndex = previousId is null ? -1 : _model.Entries.ToList().FindIndex(entry => entry.Id == previousId && entry.Enabled);
        var first = _model.Entries.ToList().FindIndex(entry => entry.Enabled);
        SelectedIndex = -1;
        Select(previousIndex >= 0 ? previousIndex : first);
        IsOpen = true;
        Show();
        if (!wasOpen)
        {
            _openedFrame = Engine.GetProcessFrames();
        }
        if (!wasOpen || !samePage) _scroll.ScrollVertical = 0;
        // A text editor outside the modal must not keep receiving Enter/Escape.
        if (IsInsideTree()) GrabFocus();
    }

    public bool HandleKeyInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey key) return false;
        if (!key.Pressed) return true;
        var code = key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;
        switch (code)
        {
            case Key.Up: case Key.W: MoveSelection(-1); break;
            case Key.Down: case Key.S: MoveSelection(1); break;
            case Key.Enter: case Key.KpEnter: case Key.Space:
                if (!key.Echo) Activate(SelectedIndex, _generation);
                break;
            case Key.Escape:
                if (!key.Echo) RequestBack();
                break;
        }
        return true;
    }

    public void Close()
    {
        IsOpen = false;
        _model = null;
        SelectedIndex = -1;
        _openedFrame = null;
        _generation++;
        Hide();
        if (!IsInsideTree()) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus is not null && (ReferenceEquals(focus, this) || IsAncestorOf(focus))) focus.ReleaseFocus();
    }

    private void MoveSelection(int delta)
    {
        if (_model is not { Entries.Count: > 0 } model) return;
        var index = SelectedIndex;
        for (var step = 0; step < model.Entries.Count; step++)
        {
            index = (index + delta + model.Entries.Count) % model.Entries.Count;
            if (!model.Entries[index].Enabled) continue;
            Select(index);
            if (IsInsideTree()) _scroll.EnsureControlVisible(_rows[index]);
            return;
        }
    }

    private void Select(int index)
    {
        if (_model is null) return;
        SelectedIndex = index >= 0 && index < _model.Entries.Count && _model.Entries[index].Enabled ? index : -1;
        for (var row = 0; row < _rows.Count; row++) _rows[row].SetPressedNoSignal(row == SelectedIndex);
        _hint.Text = SelectedIndex >= 0 ? _model.Entries[SelectedIndex].Hint ?? "" : "";
    }

    private void Activate(int index, int generation)
    {
        if (!IsOpen || generation != _generation || _model is null || index < 0 || index >= _model.Entries.Count ||
            !_model.Entries[index].Enabled || !BeginAction()) return;
        EntryActivated?.Invoke(_model.Entries[index].Id);
    }

    private void RequestBack()
    {
        if (IsOpen && BeginAction()) BackRequested?.Invoke();
    }

    private bool BeginAction()
    {
        var frame = Engine.GetProcessFrames();
        if (_openedFrame == frame || _lastActionFrame == frame) return false;
        // Preserve this across Render/Close: one confirmation must not activate a
        // newly rendered child page or its return button in the same host frame.
        _lastActionFrame = frame;
        return true;
    }

    private void EnsureUi()
    {
        if (_built) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = new Color(0.025f, 0.055f, 0.04f, 0.94f), MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(veil);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color("14251f"), BorderColor = new Color("927844"),
            ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 22, ContentMarginBottom = 20
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        card.AddThemeStyleboxOverride("panel", style);
        center.AddChild(card);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        card.AddChild(column);
        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 38);
        _title.AddThemeColorOverride("font_color", new Color("d9bd79"));
        column.AddChild(_title);
        _subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(660, 0) };
        _subtitle.AddThemeColorOverride("font_color", new Color("a3b5a6"));
        column.AddChild(_subtitle);
        column.AddChild(new HSeparator());
        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(660, 330), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FocusMode = FocusModeEnum.None
        };
        column.AddChild(_scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 16);
        _scroll.AddChild(content);
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(640, 0) };
        _body.AddThemeColorOverride("font_color", new Color("dfdbc8"));
        content.AddChild(_body);
        _entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _entries.AddThemeConstantOverride("separation", 7);
        content.AddChild(_entries);
        _hint = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(660, 25), Modulate = new Color("b9bda7")
        };
        column.AddChild(_hint);
        _error = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(660, 0), Modulate = new Color("e5ab8b")
        };
        column.AddChild(_error);
        var footer = new HBoxContainer();
        column.AddChild(footer);
        footer.AddChild(new Label { Text = "↑↓ / W、S 选择    Enter / 空格 确认", SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = new Color("a3b5a6") });
        _back = new Button { Text = "返回 · Esc", FocusMode = FocusModeEnum.None };
        _back.Pressed += RequestBack;
        footer.AddChild(_back);
        _built = true;
    }
}
