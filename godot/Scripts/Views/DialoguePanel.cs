using Godot;
using JinYong.Core.Events;

namespace JinYong.Host;

/// <summary>Legacy 320x200 dialogue geometry with modern fonts and mouse hit areas.</summary>
public partial class DialoguePanel : Control
{
    private Control _canvas = null!;
    private Panel _textFrame = null!, _portraitFrame = null!;
    private RichTextLabel _body = null!;
    private TextureRect _portrait = null!;
    private Label _page = null!;
    private readonly List<Button> _choiceButtons = [];
    private EventPresentation? _presentation;
    private bool _built;
    private int _selectedChoice, _generation;
    private ulong? _shownFrame, _lastActionFrame;

    public event Action<long>? ContinueRequested;
    public event Action<long, int>? ChoiceRequested;
    public event Action<long>? ChoiceCancelRequested;
    public long? Token { get; private set; }

    public DialoguePanel()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        EnsureUi();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += LayoutCanvas;
        LayoutCanvas();
    }

    public void Present(EventPresentation? presentation, Texture2D? portrait = null, string? speaker = null)
    {
        EnsureUi();
        if (presentation is null)
        {
            Hide(); Token = null; _presentation = null; _portrait.Texture = null; _generation++;
            return;
        }
        if (Token == presentation.Token && Visible) return;
        Token = presentation.Token; _presentation = presentation; _generation++;
        _shownFrame = Engine.GetProcessFrames();
        foreach (var button in _choiceButtons) { _textFrame.RemoveChild(button); button.QueueFree(); }
        _choiceButtons.Clear(); _selectedChoice = 0;
        _portrait.Texture = portrait;
        _portraitFrame.TooltipText = speaker ?? "";
        _portraitFrame.Hide();
        _body.Text = presentation.Text;
        _body.ScrollToLine(0);
        _body.ScrollActive = presentation.Kind != EventPresentationKind.Dialogue;
        _body.AddThemeColorOverride("default_color", presentation.Kind == EventPresentationKind.Error
            ? new Color("ffffff") : new Color("dedede"));

        if (presentation.Kind == EventPresentationKind.Dialogue)
        {
            // OpenLegend SceneSession::render, styles0..5. Fonts are deliberately modern.
            var (text, head) = presentation.Style switch
            {
                1 => (new Vector2(8, 130), new Vector2(237, 125)),
                2 => (new Vector2(94, 17), new Vector2(-1, -1)),
                3 => (new Vector2(8, 130), new Vector2(-1, -1)),
                4 => (new Vector2(8, 17), new Vector2(237, 12)),
                5 => (new Vector2(94, 130), new Vector2(23, 125)),
                _ => (new Vector2(94, 17), new Vector2(23, 12))
            };
            PlaceTextFrame(new Rect2(text, new Vector2(218, 57)), new Vector2(13, 3), new Vector2(192, 51));
            if (portrait is not null && head.X >= 0)
            {
                _portraitFrame.Position = head; _portraitFrame.Size = new Vector2(60, 62);
                _portraitFrame.Show();
            }
        }
        else if (presentation.Kind == EventPresentationKind.Choice)
        {
            var options = presentation.Choices ?? [];
            var height = 57 + 18 * options.Count;
            PlaceTextFrame(new Rect2(62, 18, 250, height), new Vector2(8, 5), new Vector2(234, 43));
            var generation = _generation;
            for (var index = 0; index < options.Count; index++)
            {
                var option = index;
                var token = presentation.Token;
                var button = new Button
                {
                    Name = $"DialogueChoice{index}", Text = options[index].Text,
                    Position = new Vector2(8, 50 + 18 * index), Size = new Vector2(234, 18),
                    FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
                };
                button.AddThemeFontSizeOverride("font_size", 13);
                foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
                    button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
                button.Pressed += () =>
                {
                    if (generation != _generation) return;
                    _selectedChoice = option; HighlightChoice();
                    if (BeginAction(token)) ChoiceRequested?.Invoke(token, option);
                };
                button.MouseEntered += () =>
                {
                    if (generation == _generation) { _selectedChoice = option; HighlightChoice(); }
                };
                button.GuiInput += input =>
                {
                    if (generation == _generation && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                    { CancelSelection(); button.AcceptEvent(); }
                };
                _textFrame.AddChild(button); _choiceButtons.Add(button);
            }
            HighlightChoice();
        }
        else
        {
            // Notices remain compact; extended MOD/errors can be read with the wheel.
            PlaceTextFrame(new Rect2(54, 40, 212, 68), new Vector2(8, 5), new Vector2(196, 58));
        }
        _textFrame.TooltipText = presentation.Kind == EventPresentationKind.Choice
            ? "点击选项 · 方向键选择 · Enter 确认" + (presentation.CanCancel ? " · 右键返回" : "")
            : "点击继续 · Enter / 空格";
        _page.Text = presentation.Kind == EventPresentationKind.Choice
            ? "" : presentation.PageCount > 1 ? $"{presentation.Page}/{presentation.PageCount} ▾" : "▾";
        _page.Position = _textFrame.Position + new Vector2(_textFrame.Size.X - 34, _textFrame.Size.Y + 1);
        _page.Visible = presentation.Kind != EventPresentationKind.Choice;
        Show(); LayoutCanvas();
    }

    public void MoveChoice(int delta)
    {
        if (_presentation?.Choices is not { Count: > 0 } options) return;
        _selectedChoice = (_selectedChoice + delta % options.Count + options.Count) % options.Count;
        HighlightChoice();
    }

    public void ConfirmSelection()
    {
        if (_presentation is { Kind: EventPresentationKind.Choice } choice && BeginAction(choice.Token))
            ChoiceRequested?.Invoke(choice.Token, _selectedChoice);
    }

    public void CancelSelection()
    {
        if (_presentation is { Kind: EventPresentationKind.Choice, CanCancel: true } choice && BeginAction(choice.Token))
            ChoiceCancelRequested?.Invoke(choice.Token);
    }

    private bool BeginAction(long token)
    {
        var frame = Engine.GetProcessFrames();
        if (!Visible || Token != token || _shownFrame == frame || _lastActionFrame == frame) return false;
        _lastActionFrame = frame;
        return true;
    }

    private void FrameInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } mouse || _presentation is not { } presentation) return;
        if (mouse.ButtonIndex == MouseButton.Right) CancelSelection();
        else if (mouse.ButtonIndex == MouseButton.Left && presentation.Kind != EventPresentationKind.Choice && BeginAction(presentation.Token))
            ContinueRequested?.Invoke(presentation.Token);
    }

    private void HighlightChoice()
    {
        for (var index = 0; index < _choiceButtons.Count; index++)
        {
            var color = index == _selectedChoice ? new Color("efefef") : new Color("de5900");
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
                _choiceButtons[index].AddThemeColorOverride(state, color);
        }
    }

    private void PlaceTextFrame(Rect2 rectangle, Vector2 textPosition, Vector2 textSize)
    {
        _textFrame.Position = rectangle.Position; _textFrame.Size = rectangle.Size;
        _body.Position = textPosition; _body.Size = textSize;
    }

    private void LayoutCanvas()
    {
        if (!_built || Size.X <= 0 || Size.Y <= 0) return;
        var scale = Math.Min(Size.X / 320f, Size.Y / 200f);
        _canvas.Scale = new Vector2(scale, scale);
        _canvas.Position = (Size - new Vector2(320, 200) * scale) / 2;
    }

    private static StyleBoxFlat FrameStyle() => new()
    {
        BgColor = new Color(0.035f, 0.035f, 0.045f, 0.82f),
        BorderColor = new Color("dedede"),
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1
    };

    private Panel CreateFrame()
    {
        var panel = new Panel { MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", FrameStyle());
        panel.GuiInput += FrameInput;
        _canvas.AddChild(panel);
        var inner = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        inner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        inner.OffsetLeft = inner.OffsetTop = 2; inner.OffsetRight = inner.OffsetBottom = -2;
        var border = FrameStyle(); border.BgColor = Colors.Transparent; border.BorderColor = new Color("707070");
        inner.AddThemeStyleboxOverride("panel", border); panel.AddChild(inner);
        return panel;
    }

    private void EnsureUi()
    {
        if (_built) return;
        _canvas = new Control { Size = new Vector2(320, 200), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_canvas);
        _textFrame = CreateFrame();
        _body = new RichTextLabel
        {
            BbcodeEnabled = false, FitContent = false, ScrollActive = false,
            MouseFilter = MouseFilterEnum.Pass, SelectionEnabled = false
        };
        _body.AddThemeFontSizeOverride("normal_font_size", 13);
        _body.AddThemeConstantOverride("line_separation", -3);
        _body.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _body.AddThemeConstantOverride("shadow_offset_x", 1);
        _body.AddThemeConstantOverride("shadow_offset_y", 1);
        _textFrame.AddChild(_body);
        _portraitFrame = CreateFrame();
        _portrait = new TextureRect
        {
            Position = new Vector2(2, 2), Size = new Vector2(56, 58), MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest
        };
        _portraitFrame.AddChild(_portrait);
        _page = new Label
        {
            Size = new Vector2(34, 10), HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color("c8c8c8")
        };
        _page.AddThemeFontSizeOverride("font_size", 8);
        _canvas.AddChild(_page);
        _built = true;
    }
}

