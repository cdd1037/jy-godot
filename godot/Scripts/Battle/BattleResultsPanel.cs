using Godot;

namespace JinYong.Host;

/// <summary>A modal view of frozen results. The owner handles confirmation and all game state.</summary>
public partial class BattleResultsPanel : Control
{
    private Label _title = null!, _summary = null!, _error = null!;
    private VBoxContainer _entries = null!;
    private ScrollContainer _errorScroll = null!;
    private Button _continue = null!;
    private bool _built, _confirming;

    public bool IsOpen { get; private set; }
    public event Action? ContinueRequested;
    internal Button ContinueButton { get { EnsureUi(); return _continue; } }

    public BattleResultsPanel()
    {
        Name = "BattleResults";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;
    }

    public override void _Ready() => EnsureUi();

    public void ShowResults(BattleResultsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureUi();
        ClearEntries();
        _title.Text = model.Title;
        _summary.Text = model.Summary;
        foreach (var section in model.Sections)
        {
            var heading = new Label { Text = section.Title };
            heading.AddThemeFontSizeOverride("font_size", 10);
            heading.AddThemeColorOverride("font_color", new Color("e6d2a4"));
            _entries.AddChild(heading);
            _entries.AddChild(new Label
            {
                Text = string.Join("\n", section.Lines), CustomMinimumSize = new Vector2(280, 0),
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
            _entries.AddChild(new HSeparator());
        }
        if (model.Sections.Count == 0)
            _entries.AddChild(new Label { Text = "此战没有升级、修炼或制作成果。" });
        _error.Text = ""; _errorScroll.Hide();
        SetConfirming(false);
        IsOpen = true;
        Show();
        if (IsInsideTree()) _continue.GrabFocus();
    }

    /// <summary>Updates the current sheet without opening it or changing confirmation state.</summary>
    public void ShowError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        EnsureUi();
        _error.Text = message; _errorScroll.Visible = !string.IsNullOrEmpty(message);
    }

    public void SetConfirming(bool confirming)
    {
        EnsureUi();
        _confirming = confirming;
        _continue.Disabled = confirming;
    }

    public void Close()
    {
        IsOpen = false;
        Hide();
        _confirming = false;
        if (_built) _continue.Disabled = false;
        if (!IsInsideTree()) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus is not null && IsAncestorOf(focus)) focus.ReleaseFocus();
    }

    private void RequestContinue()
    {
        if (IsOpen && !_confirming) ContinueRequested?.Invoke();
    }

    private void ClearEntries()
    {
        foreach (var child in _entries.GetChildren())
        {
            _entries.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void EnsureUi()
    {
        if (_built) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.88f), MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(veil);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(296, 0) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color("192720"), ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 4, ContentMarginBottom = 4
        };
        style.SetCornerRadiusAll(2);
        card.AddThemeStyleboxOverride("panel", style); center.AddChild(card);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 3); card.AddChild(column);
        _title = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; _title.AddThemeFontSizeOverride("font_size", 12); column.AddChild(_title);
        _summary = new Label { CustomMinimumSize = new Vector2(280, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_summary);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(280, 88), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        column.AddChild(scroll);
        _entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _entries.AddThemeConstantOverride("separation", 3); scroll.AddChild(_entries);
        _error = new Label
        {
            CustomMinimumSize = new Vector2(280, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color("e6b89d")
        };
        _errorScroll = new ScrollContainer { CustomMinimumSize = new Vector2(280, 16), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(_errorScroll); _errorScroll.AddChild(_error); _error.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _errorScroll.Hide();
        _continue = new Button { Name = "ContinueBattleResults", Text = "继续剧情", CustomMinimumSize = new Vector2(280, 16) };
        _continue.Pressed += RequestContinue;
        column.AddChild(_continue);
        column.AddChild(new Label { Text = "Enter / 空格继续", Modulate = new Color("94a78f") });
        _built = true;
    }
}
