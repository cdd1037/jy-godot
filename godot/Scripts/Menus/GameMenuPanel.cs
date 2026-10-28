using Godot;

namespace JinYong.Host;

/// <summary>A model-only modal page, shared by title, game, selection and information menus.</summary>
public partial class GameMenuPanel : Control
{
    private Label _title = null!, _subtitle = null!, _body = null!, _hint = null!, _error = null!;
    private VBoxContainer _entries = null!;
    private ScrollContainer _scroll = null!;
    private Button _back = null!;
    private Control _canvas = null!;
    private PanelContainer _card = null!, _mainBackdrop = null!;
    public Control? LayoutTarget { get; set; }
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

    public override void _Process(double delta) { if (IsOpen) UpdateLayout(); }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { RequestBack(); AcceptEvent(); return; }
        if (HandleKeyInput(input)) AcceptEvent();
    }

    public void Render(GameMenuModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(model.Entries);
        EnsureUi();
        var wasOpen = IsOpen;
        var samePage = _model?.Title == model.Title && _model?.Layout == model.Layout;
        var compact = model.Layout is GameMenuLayout.Main or GameMenuLayout.System or GameMenuLayout.Party;
        if (compact) model = model with { Entries = model.Entries.Where(entry => entry.Id != "back").ToArray() };
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
        _title.Visible = !compact;
        _subtitle.Text = model.Subtitle;
        _subtitle.Visible = !compact && !string.IsNullOrEmpty(model.Subtitle);
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
                FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 20),
                TooltipText = entry.Hint ?? ""
            };
            row.AddThemeFontSizeOverride("font_size", compact ? 14 : 12);
            StyleRow(row);
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
        UpdateLayout();
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
        for (var row = 0; row < _rows.Count; row++)
        {
            _rows[row].SetPressedNoSignal(row == SelectedIndex);
            _rows[row].AddThemeColorOverride("font_color", row == SelectedIndex ? new Color("eeeeee") : new Color("eea050"));
            _rows[row].AddThemeColorOverride("font_pressed_color", new Color("eeeeee"));
        }
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

    private static StyleBoxFlat PanelStyle()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.08f, 0.075f, 0.87f), BorderColor = new Color("c8c8c0"),
            ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 5, ContentMarginBottom = 5
        };
        box.SetBorderWidthAll(1);
        return box;
    }

    private static void StyleRow(Button button)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color", new Color("eea050"));
        button.AddThemeColorOverride("font_hover_color", new Color("ffffff"));
        button.AddThemeColorOverride("font_disabled_color", new Color("777770"));
        button.AddThemeColorOverride("font_shadow_color", Colors.Black);
        button.AddThemeConstantOverride("shadow_offset_x", 1);
        button.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    private void UpdateLayout()
    {
        if (_model is null) return;
        var rect = LayoutTarget?.GetGlobalRect() ?? GetGlobalRect();
        _canvas.Position = rect.Position - GlobalPosition;
        var compact = _model.Layout is GameMenuLayout.Main or GameMenuLayout.System or GameMenuLayout.Party;
        var box = _model.Layout switch
        {
            GameMenuLayout.Main => new Rect2(20, 18, 42, 12 + 20 * _rows.Count),
            GameMenuLayout.System => new Rect2(70, 18, 42, 12 + 20 * _rows.Count),
            GameMenuLayout.Party => new Rect2(68, 18, 92, Math.Max(32, 12 + 20 * _rows.Count)),
            GameMenuLayout.Title => new Rect2(40, 4, 240, 190),
            _ => new Rect2(56, 18, 208, 166)
        };
        _card.Position = box.Position;
        _card.Size = box.Size;
        _scroll.CustomMinimumSize = compact ? new Vector2(box.Size.X - 8, 20 * _rows.Count) : new Vector2(0, 30);
        _mainBackdrop.Visible = _model.Layout == GameMenuLayout.System;
        _back.Visible = !compact && _model.Layout != GameMenuLayout.Title;
        // Supplementary prompts float outside compact DOS-style lists.
        _hint.Position = new Vector2(8, 184);
        _hint.Size = new Vector2(304, 14);
    }

    private void EnsureUi()
    {
        if (_built) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // All UI coordinates are native design pixels. Viewport canvas_items
        // stretching owns window scaling and font oversampling.
        _canvas = new Control { Size = new Vector2(320, 200), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_canvas);
        _mainBackdrop = new PanelContainer { Position = new Vector2(20, 18), Size = new Vector2(42, 132), MouseFilter = MouseFilterEnum.Ignore };
        _mainBackdrop.AddThemeStyleboxOverride("panel", PanelStyle());
        _canvas.AddChild(_mainBackdrop);
        var mainRows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        mainRows.AddThemeConstantOverride("separation", 0); _mainBackdrop.AddChild(mainRows);
        foreach (var text in new[] { "医疗", "解毒", "物品", "状态", "离队", "系统" })
        {
            var label = new Label { Text = text, CustomMinimumSize = new Vector2(0, 20), MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AddThemeColorOverride("font_color", new Color(text == "系统" ? "eeeeee" : "eea050"));
            mainRows.AddChild(label);
        }
        _card = new PanelContainer();
        _card.AddThemeStyleboxOverride("panel", PanelStyle()); _canvas.AddChild(_card);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 0); _card.AddChild(column);
        _title = new Label(); _title.AddThemeFontSizeOverride("font_size", 20); _title.AddThemeColorOverride("font_color", new Color("eea050")); column.AddChild(_title);
        _subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _subtitle.AddThemeFontSizeOverride("font_size", 9); column.AddChild(_subtitle);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = FocusModeEnum.None, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(_scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 0); _scroll.AddChild(content);
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _body.AddThemeFontSizeOverride("font_size", 10); content.AddChild(_body);
        _entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _entries.AddThemeConstantOverride("separation", 0); content.AddChild(_entries);
        _error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _error.AddThemeFontSizeOverride("font_size", 9); _error.AddThemeColorOverride("font_color", new Color("efb0a0")); column.AddChild(_error);
        _back = new Button { Text = "返回 · Esc / 右键", FocusMode = FocusModeEnum.None }; _back.AddThemeFontSizeOverride("font_size", 9); StyleRow(_back); _back.Pressed += RequestBack; column.AddChild(_back);
        _hint = new Label { Text = "", MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart }; _hint.AddThemeFontSizeOverride("font_size", 8); _canvas.AddChild(_hint);
        _built = true;
    }
}
