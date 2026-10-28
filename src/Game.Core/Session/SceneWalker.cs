using JinYong.Core.Maps;

namespace JinYong.Core.Session;

public sealed record WalkingSettings(double StepSeconds = 0.06, double RepeatDelaySeconds = 0.5,
    double StandingDelaySeconds = 1);

/// <summary>Manual walking with adjustable pacing, independent of display frame rate.</summary>
public sealed class SceneWalker
{
    private readonly GameSession _session;
    private readonly List<Direction> _held = [];
    private readonly HashSet<Direction> _blocked = [];
    private double _now, _start, _end, _repeatAt, _nextStepAt, _lastActivity;
    private bool _pending, _arrival, _attempted;

    public SceneWalker(GameSession session, WalkingSettings? settings = null)
    {
        _session = session;
        Settings = settings ?? new();
        if (!double.IsFinite(Settings.StepSeconds) || Settings.StepSeconds <= 0 ||
            !double.IsFinite(Settings.RepeatDelaySeconds) || Settings.RepeatDelaySeconds < 0 ||
            !double.IsFinite(Settings.StandingDelaySeconds) || Settings.StandingDelaySeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings));
        From = To = session.Player.Position;
    }

    public WalkingSettings Settings { get; }
    public GridPosition From { get; private set; }
    public GridPosition To { get; private set; }
    public bool IsMoving => _arrival;
    public double Progress => !_arrival ? 1 : Math.Clamp((_now - _start) / Settings.StepSeconds, 0, 1);

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _now += seconds;
        _attempted = false;
        if (_arrival && _now + 1e-10 >= _end)
        {
            _arrival = false;
            _session.CompleteMovement();
        }
        if (!_arrival && To != _session.Player.Position) From = To = _session.Player.Position;
    }

    /// <summary>Held directions in press order; the newest direction wins a chord.</summary>
    public MovementResult? ReadInput(IReadOnlyList<Direction> held, bool enabled)
    {
        enabled &= !_session.IsEventRunning;
        _blocked.RemoveWhere(direction => !held.Contains(direction));
        if (!enabled)
        {
            foreach (var direction in held) _blocked.Add(direction);
            _held.Clear();
            _pending = false;
            _lastActivity = _now;
            return null;
        }

        var previous = _held.Count > 0 ? _held[^1] : (Direction?)null;
        var newPress = false;
        _held.RemoveAll(direction => !held.Contains(direction));
        foreach (var direction in held)
        {
            if (_blocked.Contains(direction) || _held.Contains(direction)) continue;
            if (_held.Count == 0) _repeatAt = _now + Settings.RepeatDelaySeconds;
            _held.Add(direction);
            _pending = true;
            newPress = true;
        }
        if (_held.Count == 0) _pending = false;
        else if (!newPress && previous is { } active && !_held.Contains(active))
        {
            // Releasing the active key falls back without another initial delay.
            _pending = true;
            _repeatAt = _now;
        }

        var due = _pending ? _nextStepAt : Math.Max(_nextStepAt, _repeatAt);
        if (!IsMoving && !_attempted && _held.Count > 0 && _now + 1e-10 >= due)
        {
            // Preserve fractional display-frame time, but do not replay missed
            // movement steps after a stall.
            _start = _pending || _now - due >= Settings.StepSeconds ? _now : due;
            _end = _nextStepAt = _start + Settings.StepSeconds;
            _pending = false;
            _attempted = true;
            var result = _session.TryMove(_held[^1]);
            From = result.From;
            To = result.To;
            _arrival = result.Moved;
            _lastActivity = _arrival ? _end : _now;
            return result;
        }
        if (_held.Count == 0 && !IsMoving && _now - _lastActivity + 1e-10 >= Settings.StandingDelaySeconds)
            _session.StandAfterWalking();
        return null;
    }
}
