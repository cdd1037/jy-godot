using JinYong.Core.Maps;

namespace JinYong.Core.Events;

public sealed partial class EventRunner
{
    // OpenLegend's picture animation waits two BIOS ticks for each even frame.
    public static readonly TimeSpan AnimationFrameTime = TimeSpan.FromSeconds(2.0 * 65536 / 1193182);
    public static readonly TimeSpan ScriptedWalkFrameTime = TimeSpan.FromSeconds(3.0 * 65536 / 1193182);
    private int _animationEvent, _nextFrame, _endFrame;
    private bool _animating, _refresh;
    private TimeSpan _animationWait;
    private TimeSpan? _wait;
    private bool _fading;
    private int _fadeTarget;
    private TimeSpan _fadeElapsed, _fadeDuration;
    private EventCommand? _fadeCommand;
    private EventOperation? _sceneOperation;
    private EventCommand? _sceneCommand;
    private int _sceneX, _sceneY, _targetX, _targetY;
    private int _pairFirstEvent, _pairFirstFrame, _pairFirstEnd, _pairSecondEvent, _pairSecondFrame;
    private int _statuePhase, _statueCounter, _statuePlayerPicture;
    private TimeSpan _sceneWait;

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (IsRetired || Pending is not null || PendingBattle is not null || PendingLifecycle is not null ||
            PendingShop is not null || PendingEnding is not null || _program is null) return;
        if (_sceneOperation is not null)
        {
            try
            {
                _sceneWait -= elapsed;
                UpdateScriptedProgress();
                while (_sceneWait <= TimeSpan.Zero)
                {
                    var overdue = -_sceneWait;
                    if (!NextSceneFrame())
                    {
                        if (!_refresh) Pump();
                        return;
                    }
                    _sceneWait -= overdue;
                    UpdateScriptedProgress();
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException)
            { Fail(error, _sceneCommand, _program?.Id); }
            return;
        }
        if (_fading)
        {
            try
            {
                var fadeRemaining = _fadeDuration - _fadeElapsed;
                _fadeElapsed = elapsed >= fadeRemaining ? _fadeDuration : _fadeElapsed + elapsed;
                var progress = _fadeElapsed.TotalSeconds / _fadeDuration.TotalSeconds;
                _world.SetFade(_fadeTarget == 1 ? progress : 1 - progress);
                if (_fadeElapsed < _fadeDuration) return;
                _fading = false; _fadeCommand = null;
                // A subsequent fade starts here but consumes only future elapsed
                // time, like the runner's wait and animation continuations.
                Pump();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException)
            { Fail(error, _fadeCommand, _program?.Id); }
            return;
        }
        if (_wait is { } remaining)
        {
            _wait = remaining - elapsed;
            if (_wait > TimeSpan.Zero) return;
            _wait = null; Pump(); return;
        }
        if (_refresh) { _refresh = false; Pump(); return; }
        if (!_animating) return;
        _animationWait -= elapsed;
        while (_animationWait <= TimeSpan.Zero)
        {
            if (_nextFrame > _endFrame) { _animating = false; Pump(); return; }
            _world.SetPicture(_animationEvent, _nextFrame);
            _nextFrame += 2;
            _animationWait += AnimationFrameTime;
        }
    }

    private void ClearTimedState()
    {
        if (_sceneOperation == EventOperation.ScriptedWalk) _world.CancelScriptedMotion();
        _sceneOperation = null; _sceneCommand = null; _sceneWait = TimeSpan.Zero;
        _animating = _refresh = _fading = false;
        _wait = null; _fadeCommand = null;
        PendingBattle = null; _battleCommand = null;
        PendingLifecycle = null;
        PendingShop = null; PendingEnding = null; _tournament = null;
        _shopCommand = null; _shopPhase = default; _shopPurchased = false;
        _fadeElapsed = _fadeDuration = TimeSpan.Zero;
    }

    private void UpdateScriptedProgress()
    {
        if (_sceneOperation == EventOperation.ScriptedWalk)
            _world.UpdateScriptedMotion(Math.Clamp(1 - _sceneWait.TotalSeconds / ScriptedWalkFrameTime.TotalSeconds, 0, 1));
    }

    // One continuation inside the existing runner, not another instruction VM.
    // References: OpenLegend Z_DAT/0x2ED8D.md, 0x2F171.md and 0x2F9F2.md.
    private bool NextSceneFrame()
    {
        if (_sceneOperation == EventOperation.StatueAnimation)
        {
            // Z_DAT/0x301D1: six player frames, then 29 synchronized statue
            // frames. Only the player picture freezes when it reaches 7688.
            if (_statuePhase == 0 && _statueCounter > 7674)
            { _statuePhase = 1; _statueCounter = 0; }
            if (_statuePhase == 1 && _statueCounter > 56)
            { _sceneOperation = null; _sceneCommand = null; return false; }
            if (_statuePhase == 0)
            {
                _statuePlayerPicture = _statueCounter;
                _world.SetPictureWrapped(-1, _statuePlayerPicture);
            }
            else
            {
                if (_statuePlayerPicture < 7688)
                {
                    _statuePlayerPicture = _statueCounter + 7676;
                    _world.SetPictureWrapped(-1, _statuePlayerPicture);
                }
                _world.SetPictureWrapped(2, _statueCounter + 7690);
                _world.SetPictureWrapped(3, _statueCounter + 7748);
                _world.SetPictureWrapped(4, _statueCounter + 7806);
            }
            _statueCounter += 2;
            _sceneWait = AnimationFrameTime;
            return true;
        }
        if (_sceneOperation is EventOperation.AnimatePair or EventOperation.Ending)
        {
            var ending = _sceneOperation == EventOperation.Ending;
            if (_pairFirstFrame > _pairFirstEnd)
            {
                if (ending)
                {
                    _world.BeginEnding();
                    PendingEnding = new(++_token);
                }
                _sceneOperation = null; _sceneCommand = null; return false;
            }
            // In 62, -1 means skip that event, not animate the player.
            if (!ending || _pairFirstEvent != -1) _world.SetPictureWrapped(_pairFirstEvent, _pairFirstFrame);
            if (!ending || _pairSecondEvent != -1) _world.SetPictureWrapped(_pairSecondEvent, _pairSecondFrame);
            _pairFirstFrame += 2; _pairSecondFrame += 2;
            _sceneWait = AnimationFrameTime;
            return true;
        }
        var horizontal = _sceneX != _targetX;
        if (!horizontal && _sceneY == _targetY)
        {
            if (_sceneOperation == EventOperation.ScriptedWalk)
            {
                _world.EndScriptedWalk();
                _refresh = true; // Final standing present, including zero-distance walks.
            }
            _sceneOperation = null; _sceneCommand = null;
            return false;
        }
        var step = horizontal ? Math.Sign(_targetX - _sceneX) : Math.Sign(_targetY - _sceneY);
        if (_sceneOperation == EventOperation.PanCamera)
        {
            _world.SetCameraAxis(horizontal, horizontal ? _sceneX : _sceneY);
            _sceneWait = AnimationFrameTime;
        }
        else
        {
            var direction = horizontal ? (step < 0 ? Direction.Left : Direction.Right) :
                step < 0 ? Direction.Up : Direction.Down;
            _world.BeginScriptedStep(direction);
            _sceneWait = ScriptedWalkFrameTime;
        }
        if (horizontal) _sceneX += step; else _sceneY += step;
        return true;
    }
}
