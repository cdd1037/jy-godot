using Godot;
using JinYong.Core.Events;

namespace JinYong.Host;

public partial class DialoguePanel : PanelContainer
{
    private TextureRect _portrait = null!;
    private Label _speaker = null!, _body = null!, _page = null!;
    private HBoxContainer _row = null!;
    private VBoxContainer _choices = null!;
    private Button _next = null!;
    public event Action<long>? ContinueRequested;
    public event Action<long, int>? ChoiceRequested;
    public event Action<long>? ChoiceCancelRequested;
    private EventPresentation? _presentation;
    private int _selectedChoice;
    public long? Token { get; private set; }

    public override void _Ready()
    {
        AnchorLeft = 0.025f; AnchorRight = 0.975f;
        AnchorTop = AnchorBottom = 1;
        OffsetTop = -232; OffsetBottom = -14;
        MouseFilter = MouseFilterEnum.Stop;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.1f, 0.08f, 0.97f), BorderColor = new Color("a28d5e"),
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 15, ContentMarginBottom = 12
        };
        style.SetCornerRadiusAll(8); style.SetBorderWidthAll(1);
        AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);
        _row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _row.AddThemeConstantOverride("separation", 18);
        column.AddChild(_row);
        _portrait = new TextureRect
        {
            CustomMinimumSize = new Vector2(100, 105), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest
        };
        _row.AddChild(_portrait);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 8);
        _row.AddChild(text);
        _speaker = new Label();
        _speaker.AddThemeFontSizeOverride("font_size", 20);
        _speaker.AddThemeColorOverride("font_color", new Color("e2c68d"));
        text.AddChild(_speaker);
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeFontSizeOverride("font_size", 22);
        _body.AddThemeConstantOverride("line_spacing", 5);
        text.AddChild(_body);
        _choices = new VBoxContainer { Visible = false };
        _choices.AddThemeConstantOverride("separation", 4);
        text.AddChild(_choices);
        var footer = new HBoxContainer();
        column.AddChild(footer);
        _page = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = new Color("99aa91") };
        footer.AddChild(_page);
        _next = new Button { Text = "继续  ↵", CustomMinimumSize = new Vector2(104, 34), FocusMode = FocusModeEnum.None };
        _next.Pressed += () => { if (Token is { } token) ContinueRequested?.Invoke(token); };
        footer.AddChild(_next);
        Visible = false;
    }

    public void Present(EventPresentation? presentation, Texture2D? portrait = null, string? speaker = null)
    {
        if (presentation is null)
        {
            Visible = false; Token = null; _portrait.Texture = null; _presentation = null;
            return;
        }
        if (Token == presentation.Token && Visible) return;
        Token = presentation.Token;
        _presentation = presentation;
        Visible = true;
        _portrait.Texture = portrait;
        _portrait.Visible = portrait is not null;
        _row.MoveChild(_portrait, presentation.Style == 1 ? _row.GetChildCount() - 1 : 0);
        _speaker.Text = presentation.Kind switch
        {
            EventPresentationKind.Item => "收入行囊",
            EventPresentationKind.Error => "剧情提示",
            EventPresentationKind.Notice => "状态变化",
            EventPresentationKind.Choice => "请选择",
            _ => speaker ?? "对话"
        };
        _body.Text = presentation.Text;
        var isChoice = presentation.Kind == EventPresentationKind.Choice;
        _choices.Visible = isChoice;
        _next.Visible = !isChoice;
        foreach (var child in _choices.GetChildren()) { _choices.RemoveChild(child); child.QueueFree(); }
        _selectedChoice = 0;
        if (isChoice)
        {
            OffsetTop = -(218 + 40 * presentation.Choices!.Count);
            for (var i = 0; i < presentation.Choices.Count; i++)
            {
                var option = i;
                var token = presentation.Token;
                var button = new Button { Text = $"{i + 1}. {presentation.Choices[i].Text}",
                    CustomMinimumSize = new Vector2(0, 34), FocusMode = FocusModeEnum.None };
                button.Pressed += () => ChoiceRequested?.Invoke(token, option);
                _choices.AddChild(button);
            }
            HighlightChoice();
        }
        else OffsetTop = -232;
        _page.Text = presentation.PageCount > 1 ? $"{presentation.Page} / {presentation.PageCount}    空格 / Enter 继续" : "空格 / Enter 继续";
        if (isChoice) _page.Text = "↑↓ 选择 · Enter 确认" + (presentation.CanCancel ? " · Esc 取消" : "");
    }

    public void MoveChoice(int delta)
    {
        if (_presentation?.Choices is not { } options) return;
        _selectedChoice = (_selectedChoice + delta + options.Count) % options.Count;
        HighlightChoice();
    }

    public void ConfirmSelection()
    {
        if (_presentation is { Kind: EventPresentationKind.Choice } choice) ChoiceRequested?.Invoke(choice.Token, _selectedChoice);
    }

    public void CancelSelection()
    {
        if (_presentation is { Kind: EventPresentationKind.Choice, CanCancel: true } choice) ChoiceCancelRequested?.Invoke(choice.Token);
    }

    private void HighlightChoice()
    {
        for (var i = 0; i < _choices.GetChildCount(); i++)
            _choices.GetChild<Button>(i).Modulate = i == _selectedChoice ? new Color("f3d894") : Colors.White;
    }
}
