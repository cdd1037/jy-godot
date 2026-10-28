using Godot;
using JinYong.Content.Ending;

namespace JinYong.Host;

/// <summary>Draws normalized ending artwork. It owns no event continuation or authoritative game state.</summary>
public partial class EndingPanel : Control
{
    private static readonly int[] CreditX = [60,60,60,60,115,115,115,115,115,115,115,115,115,115,115,115,115,105,135,56];
    private static readonly int[] CreditY = [210,386,551,698,950,1111,1255,1391,1557,1718,1772,1938,2087,2195,2335,2479,2642,2743,3055,3301];
    private EndingAssets? _assets;
    private ImageTexture[] _words = [];
    private ImageTexture? _movie;
    private int _movieIndex = -1;
    private EndingFrameModel _frame = new(EndingVisual.Black);
    private VBoxContainer _errorPanel = null!;
    private Label _error = null!, _hint = null!;
    private bool _built;

    public bool IsOpen { get; private set; }
    public event Action? RetryRequested;
    public event Action<InputEvent>? KeyInput;

    public EndingPanel()
    {
        Name = "Ending"; Visible = false; ZIndex = 120;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Nearest; ClipContents = true;
    }

    public override void _Ready() { EnsureUi(); Resized += QueueRedraw; }

    public override void _GuiInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey) return;
        KeyInput?.Invoke(input);
        AcceptEvent();
    }

    public void Present(EndingAssets assets, EndingFrameModel frame)
    {
        EnsureUi();
        if (!ReferenceEquals(_assets, assets)) BindAssets(assets);
        if (frame.Visual == EndingVisual.Movie && _movieIndex != frame.Index)
        {
            using var image = Image.CreateFromData(EndingAssets.Width, EndingAssets.Height, false, Image.Format.Rgba8, assets.FrameRgba(frame.Index));
            if (_movie is null) _movie = ImageTexture.CreateFromImage(image); else _movie.Update(image);
            _movieIndex = frame.Index;
        }
        _frame = frame;
        _errorPanel.Hide(); _hint.Visible = frame.WaitForKey;
        Open(); QueueRedraw();
    }

    public void ShowError(string message)
    {
        EnsureUi();
        _error.Text = message + "\n结局尚未完成。修复资源后点击重试，或按任意键重试。";
        _frame = new(EndingVisual.Black); _hint.Hide(); _errorPanel.Show();
        Open(); QueueRedraw();
    }

    public void Close()
    {
        IsOpen = false; Hide(); ReleaseAssets();
        if (IsInsideTree() && HasFocus()) ReleaseFocus();
    }

    private void Open()
    {
        var wasOpen = IsOpen;
        IsOpen = true; Show();
        if (!wasOpen && IsInsideTree()) GrabFocus();
    }

    public override void _Draw()
    {
        if (!IsOpen) return;
        if (_frame.Visual == EndingVisual.SceneFadeOut)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, _frame.Opacity));
            return;
        }
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
        var assets = _assets;
        if (assets is null || _frame.Visual == EndingVisual.Black) return;
        var scale = Math.Min(Size.X / EndingAssets.Width, Size.Y / EndingAssets.Height);
        var origin = (Size - new Vector2(EndingAssets.Width, EndingAssets.Height) * scale) / 2;
        var tint = new Color(1, 1, 1, _frame.Opacity);
        void Word(int index, int x, int y)
        {
            var asset = assets.Words[index];
            var topLeft = new Vector2(x - asset.OffsetX, y - asset.OffsetY);
            var logical = new Rect2(topLeft, new Vector2(asset.Width, asset.Height));
            var clipped = logical.Intersection(new Rect2(0, 0, EndingAssets.Width, EndingAssets.Height));
            if (clipped.Size.X <= 0 || clipped.Size.Y <= 0) return;
            DrawTextureRectRegion(_words[index], new Rect2(origin + clipped.Position * scale, clipped.Size * scale),
                new Rect2(clipped.Position - topLeft, clipped.Size), tint);
        }
        switch (_frame.Visual)
        {
            case EndingVisual.Title: Word(0, 94, 90); break;
            case EndingVisual.Words: Word(1, 44, 210 - _frame.Index); Word(2, 44, 313 - _frame.Index); break;
            case EndingVisual.Movie:
                if (_movie is not null) DrawTextureRect(_movie, new Rect2(origin, new Vector2(320, 200) * scale), false, tint);
                break;
            case EndingVisual.Credits:
                for (var index = 0; index < CreditX.Length; index++) Word(index + 3, CreditX[index], CreditY[index] - _frame.Index);
                break;
        }
    }

    private void BindAssets(EndingAssets assets)
    {
        var textures = new List<ImageTexture>();
        try
        {
            for (var index = 0; index < assets.Words.Count; index++)
            {
                var word = assets.Words[index];
                using var image = Image.CreateFromData(word.Width, word.Height, false, Image.Format.Rgba8, assets.WordRgba(index));
                textures.Add(ImageTexture.CreateFromImage(image));
            }
        }
        catch { foreach (var texture in textures) texture.Dispose(); throw; }
        ReleaseAssets(); _assets = assets; _words = textures.ToArray();
    }

    private void ReleaseAssets()
    {
        foreach (var texture in _words) texture.Dispose();
        _movie?.Dispose(); _movie = null; _movieIndex = -1; _words = []; _assets = null;
    }

    private void EnsureUi()
    {
        if (_built) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _hint = new Label { Text = "按任意键继续", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _hint.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide); _hint.OffsetTop = -36; AddChild(_hint); _hint.Hide();
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        _errorPanel = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) };
        _errorPanel.AddThemeConstantOverride("separation", 16); center.AddChild(_errorPanel);
        _error = new Label { CustomMinimumSize = new Vector2(600, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _errorPanel.AddChild(_error);
        var retry = new Button { Text = "重试结局", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44) };
        retry.Pressed += () => { if (IsOpen && _errorPanel.Visible) RetryRequested?.Invoke(); };
        _errorPanel.AddChild(retry); _errorPanel.Hide(); _built = true;
    }

    public override void _ExitTree() => ReleaseAssets();
}
