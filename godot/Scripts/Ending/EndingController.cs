using Godot;
using JinYong.Content.Ending;
using JinYong.Core.Session;

namespace JinYong.Host;

public enum EndingStage
{
    Idle, Failed, SceneFadeOut, TitleFadeIn, TitleHold, TitleFadeOut, WordsFadeIn, WordScroll,
    WordsFadeOut, MovieFadeIn, Movie, WaitMovie, MovieFadeOut, CreditsFadeIn, CreditsScroll,
    WaitCredits, CreditsFadeOut, Complete
}

/// <summary>
/// Terminal ending presentation, advanced only by the composition root's existing clock.
/// Source: OpenLegend Z_DAT/0x30C3D.md. Native frame counts/geometry and both key waits
/// are retained. Fade, scroll and movie durations are remake approximations, not DOS ticks.
/// </summary>
public sealed class EndingController : IDisposable
{
    private const double FadeSeconds = 0.45, TitleSeconds = 2.8, ScrollFrameSeconds = 0.03, MovieFrameSeconds = 0.05;
    private readonly GameViewHost _host;
    private readonly EndingPanel _panel;
    private GameSession? _session;
    private long _token;
    private EndingAssets? _assets;
    private int _index;
    private double _elapsed;
    private ulong _stageFrame;
    private ulong? _lastKeyFrame;
    private bool _completed, _disposed;

    public EndingStage Stage { get; private set; }
    public bool IsOpen => !_disposed && _session is not null;
    public string? Error { get; private set; }
    public event Action? Completed;

    public EndingController(GameViewHost host, EndingPanel panel)
    {
        _host = host; _panel = panel;
        host.SessionChanged += Reset; panel.RetryRequested += RetryRequested; panel.KeyInput += PanelKeyInput;
    }

    public void Advance(double delta)
    {
        if (_disposed) return;
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (_host.Session is not { } session || session.Events?.PendingEnding is not { } request)
        { if (_session is not null) Reset(); return; }
        if (!ReferenceEquals(session, _session) || _token != request.Token)
        {
            Reset(); _session = session; _token = request.Token; Retry(); return;
        }
        if (Stage == EndingStage.Failed) return;
        if (Stage == EndingStage.Complete)
        {
            // Give the final fully black frame a presentation boundary before asking the owner to quit.
            if (!_completed && Engine.GetProcessFrames() > _stageFrame) { _completed = true; Completed?.Invoke(); }
            return;
        }
        _elapsed += delta;
        switch (Stage)
        {
            case EndingStage.SceneFadeOut: After(FadeSeconds, EndingStage.TitleFadeIn); break;
            case EndingStage.TitleFadeIn: After(FadeSeconds, EndingStage.TitleHold); break;
            case EndingStage.TitleHold: After(TitleSeconds, EndingStage.TitleFadeOut); break;
            case EndingStage.TitleFadeOut: After(FadeSeconds, EndingStage.WordsFadeIn); break;
            case EndingStage.WordsFadeIn: After(FadeSeconds, EndingStage.WordScroll); break;
            case EndingStage.WordScroll: Scroll(443, ScrollFrameSeconds, EndingStage.WordsFadeOut); break;
            case EndingStage.WordsFadeOut: After(FadeSeconds, EndingStage.MovieFadeIn); break;
            case EndingStage.MovieFadeIn: After(FadeSeconds, EndingStage.Movie); break;
            case EndingStage.Movie: Scroll(221, MovieFrameSeconds, EndingStage.WaitMovie); break;
            case EndingStage.MovieFadeOut: After(FadeSeconds, EndingStage.CreditsFadeIn); break;
            case EndingStage.CreditsFadeIn: After(FadeSeconds, EndingStage.CreditsScroll); break;
            case EndingStage.CreditsScroll: Scroll(3244, ScrollFrameSeconds, EndingStage.WaitCredits); break;
            case EndingStage.CreditsFadeOut: After(FadeSeconds, EndingStage.Complete); break;
        }
        Render();
    }

    public bool HandleKeyInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey { Pressed: true, Echo: false }) return false;
        var frame = Engine.GetProcessFrames();
        if (_lastKeyFrame == frame) return true;
        _lastKeyFrame = frame;
        if (!IsCurrent()) return true;
        if (Stage == EndingStage.Failed) { Retry(); return true; }
        // The key must arrive after the waiting frame has had a chance to appear.
        if (frame <= _stageFrame) return true;
        if (Stage == EndingStage.WaitMovie) SetStage(EndingStage.MovieFadeOut, resetIndex: false);
        else if (Stage == EndingStage.WaitCredits) SetStage(EndingStage.CreditsFadeOut, resetIndex: false);
        Render(); return true;
    }

    public bool Retry()
    {
        if (!IsCurrent() || Stage is not (EndingStage.Idle or EndingStage.Failed)) return false;
        try
        {
            var library = _host.Library ?? throw new InvalidOperationException("结局需要完整的共享资源库。");
            _assets = EndingAssetsLoader.Load(library);
            Error = null; _completed = false;
            SetStage(EndingStage.SceneFadeOut); Render(); return Stage != EndingStage.Failed;
        }
        catch (Exception error)
        {
            _assets = null; Error = "结局资源加载失败：" + error.Message;
            Stage = EndingStage.Failed; _panel.ShowError(Error); GD.PushError(error.ToString()); return false;
        }
    }

    private bool IsCurrent() => !_disposed && _session is { IsRetired: false } && ReferenceEquals(_host.Session, _session) &&
        _session.Events?.PendingEnding?.Token == _token;
    private void RetryRequested() { if (Stage == EndingStage.Failed) Retry(); }
    private void PanelKeyInput(InputEvent input) => HandleKeyInput(input);
    private void After(double duration, EndingStage next) { if (_elapsed >= duration) SetStage(next); }
    private void Scroll(int count, double frameSeconds, EndingStage next)
    {
        if (_elapsed < frameSeconds) return;
        // At most one native frame per host Advance: lag cannot skip unseen words/movie frames.
        _elapsed = 0;
        if (_index + 1 < count) _index++; else SetStage(next, resetIndex: false);
    }
    private void SetStage(EndingStage stage, bool resetIndex = true)
    {
        Stage = stage; _elapsed = 0; _stageFrame = Engine.GetProcessFrames();
        if (resetIndex) _index = 0;
    }
    private void Render()
    {
        if (_assets is null) return;
        var progress = (float)Math.Clamp(_elapsed / FadeSeconds, 0, 1);
        var model = Stage switch
        {
            EndingStage.SceneFadeOut => new EndingFrameModel(EndingVisual.SceneFadeOut, Opacity: progress),
            EndingStage.TitleFadeIn => new(EndingVisual.Title, Opacity: progress),
            EndingStage.TitleHold => new(EndingVisual.Title),
            EndingStage.TitleFadeOut => new(EndingVisual.Title, Opacity: 1 - progress),
            EndingStage.WordsFadeIn => new(EndingVisual.Words, Opacity: progress),
            EndingStage.WordScroll => new(EndingVisual.Words, _index),
            EndingStage.WordsFadeOut => new(EndingVisual.Words, 442, 1 - progress),
            EndingStage.MovieFadeIn => new(EndingVisual.Movie, Opacity: progress),
            EndingStage.Movie => new(EndingVisual.Movie, _index),
            EndingStage.WaitMovie => new(EndingVisual.Movie, 220, WaitForKey: true),
            EndingStage.MovieFadeOut => new(EndingVisual.Movie, 220, 1 - progress),
            EndingStage.CreditsFadeIn => new(EndingVisual.Credits, Opacity: progress),
            EndingStage.CreditsScroll => new(EndingVisual.Credits, _index),
            EndingStage.WaitCredits => new(EndingVisual.Credits, 3243, WaitForKey: true),
            EndingStage.CreditsFadeOut => new(EndingVisual.Credits, 3243, 1 - progress),
            _ => new(EndingVisual.Black)
        };
        try { _panel.Present(_assets, model); }
        catch (Exception error)
        {
            Error = "结局画面无法显示：" + error.Message; Stage = EndingStage.Failed;
            _panel.ShowError(Error); GD.PushError(error.ToString());
        }
    }

    private void Reset()
    {
        _session = null; _token = 0; _assets = null; _index = 0; _elapsed = 0;
        _completed = false; Error = null; Stage = EndingStage.Idle; _lastKeyFrame = null;
        _panel.Close();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _host.SessionChanged -= Reset; _panel.RetryRequested -= RetryRequested; _panel.KeyInput -= PanelKeyInput;
        Reset(); _disposed = true;
    }
}
