namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    // A nominal BIOS tick for presentation, not a DOS main-loop emulator.
    // OpenLegend scene.cpp advance_event_pictures and Z_DAT/0x23AA6.md.
    private static readonly TimeSpan AmbientTick = TimeSpan.FromSeconds(65536.0 / 1193182);
    private TimeSpan _sceneAnimationElapsed, _playerIdleElapsed;
    private int _sceneAnimationCounter, _idleWaitTicks, _idleStep, _idleDelay;
    private int? _idlePicture;

    private void AdvanceSceneAnimation(TimeSpan elapsed)
    {
        _sceneAnimationElapsed += elapsed;
        // Avoid replaying an unbounded number of unseen frames after a stall.
        var ticks = (int)Math.Min(8L, _sceneAnimationElapsed.Ticks / AmbientTick.Ticks);
        if (ticks == 0) return;
        _sceneAnimationElapsed = TimeSpan.FromTicks(_sceneAnimationElapsed.Ticks % AmbientTick.Ticks);
        for (var tick = 0; tick < ticks; tick++)
        {
            _sceneAnimationCounter = (_sceneAnimationCounter + 1) % 1000;
            Scene.AdvanceEventPictures(_sceneAnimationCounter);
        }
    }

    internal void AdvancePlayerIdle(TimeSpan elapsed)
    {
        if (IsInputLocked || !IsWorld || Travel?.Snapshot.InShip == true || _walkFrameOffset != 0 || _picture is not null)
        { CancelPlayerIdle(); return; }
        _playerIdleElapsed += elapsed;
        var ticks = Math.Min(8L, _playerIdleElapsed.Ticks / AmbientTick.Ticks);
        if (ticks == 0) return;
        _playerIdleElapsed = TimeSpan.FromTicks(_playerIdleElapsed.Ticks % AmbientTick.Ticks);
        for (var tick = 0; tick < ticks; tick++)
        {
            if (_idleStep == 0)
            {
                _idleWaitTicks = Math.Min(_idleWaitTicks + 1, 51);
                // Use the sole session RNG. Cosmetic waiting therefore consumes
                // the same stream as other legacy actions, never a second RNG.
                if (_idleWaitTicks <= 50 || Events?.DrawRandom(10) != 0) continue;
                _idleStep = 2; _idleDelay = 0;
            }
            if (++_idleDelay > 3) { _idleDelay = 0; _idleStep += 2; }
            if (_idleStep > 12) { CancelPlayerIdle(); continue; }
            var direction = (int)Travel!.Snapshot.Facing;
            var picture = new[] { 5002, 5016, 5030, 5044 }[direction] + new[] { 54, 52, 50, 48 }[direction] + _idleStep;
            if (_idlePicture != picture) { _idlePicture = picture; PresentationRevision++; }
        }
    }

    internal void CancelPlayerIdle()
    {
        _playerIdleElapsed = TimeSpan.Zero;
        _idleWaitTicks = _idleStep = _idleDelay = 0;
        if (_idlePicture is null) return;
        _idlePicture = null; PresentationRevision++;
    }

    private void ResetAmbientAnimation()
    {
        CancelPlayerIdle();
        _sceneAnimationElapsed = TimeSpan.Zero;
        _sceneAnimationCounter = 0;
    }
}
