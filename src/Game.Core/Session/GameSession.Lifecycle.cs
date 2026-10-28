using JinYong.Core.Events;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    public EventLifecycleRequest? PendingLifecycle => Events?.PendingLifecycle;
    public bool IsRetired { get; private set; }

    /// <summary>
    /// Called by the host after a fully prepared replacement session/view commits.
    /// A failed load never calls this: it keeps the original death/load menu.
    /// There is deliberately no method which resumes opcode15/24 in this session.
    /// </summary>
    public void RetireForReplacement()
    {
        if (IsRetired) return;
        Events?.Retire();
        Battle = null; PendingTravel = null;
        _arrivalPending = false; _resolveAfterEvent = false; _fadingIn = false;
        ResetPresentationForTravel();
        IsRetired = true;
        PresentationRevision++;
    }
}
