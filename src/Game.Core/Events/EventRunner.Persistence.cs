using JinYong.Core.Persistence;

namespace JinYong.Core.Events;

public sealed partial class EventRunner
{
    public bool CanCaptureIdleState => !IsRetired && !IsRunning && PendingBattle is null &&
        !_animating && !_refresh && !_fading && _wait is null && _sceneOperation is null && _calls.Count == 0;

    public EventRunnerIdleSnapshot CaptureIdleState()
    {
        if (!CanCaptureIdleState) throw new InvalidOperationException("Only an idle event runner can be saved.");
        if (_token < 0 || _token == long.MaxValue) throw new InvalidOperationException("Event token is outside the restorable range.");
        return new(_random, _token, Array.AsReadOnly(_completed.ToArray()));
    }

    public void RestoreIdleState(EventRunnerIdleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!CanCaptureIdleState) throw new InvalidOperationException("An active or retired event runner cannot restore idle state.");
        ArgumentNullException.ThrowIfNull(snapshot.CompletedPrograms);
        if (snapshot.Token < 0 || snapshot.Token == long.MaxValue)
            throw new ArgumentException("Event snapshot token must be nonnegative and below Int64.MaxValue.");
        var completed = snapshot.CompletedPrograms.ToArray();
        if (completed.Any(id => id < 0 || !_catalog.Programs.ContainsKey(id)))
            throw new ArgumentException("Event snapshot references an unknown completed program.");
        _random = snapshot.RandomState; _token = snapshot.Token;
        _completed.Clear(); _completed.AddRange(completed);
        Error = null; _trace.Clear(); _sounds.Clear(); _dialogue = null;
        ClearTimedState();
    }
}
