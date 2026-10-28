using Godot;
using JinYong.Content.Audio;
using JinYong.Content.Packages;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Schedules normalized music messages; the Core session decides which song/restart to request.</summary>
public sealed class MusicController : IDisposable
{
    private readonly GameViewHost _host;
    private WindowsMidiOutput? _output;
    private GameSession? _session;
    private MidiScore? _score;
    private long _revision = -1;
    private double _tick;
    private int _cursor;
    private bool _disposed;
    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Stop(); _revision = -1; Error = null;
        }
    }

    public string? Error { get; private set; }

    public MusicController(GameViewHost host)
    {
        _host = host;
        host.SessionChanged += Reset;
    }

    public void Advance(double delta)
    {
        if (_disposed || !Enabled) return;
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        try
        {
            if (!ReferenceEquals(_session, _host.Session)) Reset();
            if (_host.Session is not { IsRetired: false } session) return;
            _session = session;
            if (_revision != session.MusicRevision)
            {
                _revision = session.MusicRevision;
                Start(session.CurrentMusicId);
            }
            if (_score is not { } score || _output is null) return;
            _tick += delta * score.TickRate;
            var end = score.LoopEndTick ?? score.EndTick;
            EmitUntil(Math.Min(_tick, end));
            if (_tick < end) return;
            // Consume a loop boundary once per host frame. A long pause must not
            // burst an unbounded number of inaudible complete songs to the device.
            var start = score.LoopStartTick ?? 0;
            _tick = start + (_tick - end) % (end - start);
            _output.Silence();
            _cursor = Array.FindIndex(score.Messages, message => message.Tick >= start);
            if (_cursor < 0) _cursor = score.Messages.Length;
            if (start == 0) _output.Reset();
            EmitUntil(_tick);
        }
        catch (Exception error)
        {
            Stop();
            Error = "音乐无法播放：" + error.Message;
            GD.PushError(error.ToString());
        }
    }

    private void Start(int? musicId)
    {
        Stop(); Error = null;
        if (musicId is null) return;
        var library = _host.Library ?? throw new InvalidOperationException("音乐需要完整资源库。");
        // Original runtime IDs are zero based; original filenames start GAME01.
        if (!library.Audio.Scores.TryGetValue(musicId.Value + 1, out var path))
            throw new InvalidDataException("资源库缺少可播放乐谱，请重新导入原版音乐。");
        if (!library.Manifest.FileHashes.ContainsKey(path)) throw new InvalidDataException("音乐文件未登记在资源库中。");
        var score = ContentJson.Read<MidiScore>(library.Resolve(path));
        score.Validate();
        _output ??= new WindowsMidiOutput();
        _output.Reset();
        _score = score; _tick = 0; _cursor = 0;
    }

    private void EmitUntil(double tick)
    {
        while (_score is { } score && _cursor < score.Messages.Length && score.Messages[_cursor].Tick <= tick)
        {
            var message = score.Messages[_cursor++];
            _output!.Send(message.Status, message.Data1, message.Data2);
        }
    }

    private void Stop()
    {
        _score = null; _tick = 0; _cursor = 0;
        // Dispose rather than reuse a potentially failed device handle.
        _output?.Dispose(); _output = null;
    }

    private void Reset()
    {
        Stop(); _session = null; _revision = -1; Error = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.SessionChanged -= Reset;
        Reset();
    }
}
