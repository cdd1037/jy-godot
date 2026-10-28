using JinYong.Core.Events;
using JinYong.Core.Maps;

namespace JinYong.Core.Session;

/// <summary>World-grid camera centre. Null Camera means ordinary player following.</summary>
public readonly record struct CameraView(double X, double Y);
public readonly record struct ScriptedMotionView(GridPosition From, GridPosition To, double Progress);

public sealed partial class GameSession
{
    public CameraView? Camera { get; private set; }
    public ScriptedMotionView? ScriptedMotion { get; private set; }

    // OpenLegend research/evidence/functions/Z_DAT/0x2ED8D.md: word wrapping
    // precedes the 0..36 origin clamp; only the current pan axis is written.
    void IEventWorld.SetCameraAxis(bool horizontal, int coordinate)
    {
        var camera = Camera ?? new CameraView(Math.Clamp(_position.X - 11, 0, 36) + 11,
            Math.Clamp(_position.Y - 11, 0, 36) + 11);
        var centre = Math.Clamp((int)unchecked((short)(coordinate - 11)), 0, 36) + 11;
        Camera = horizontal ? camera with { X = centre } : camera with { Y = centre };
        PresentationRevision++;
    }

    // Source/target in opcode30 are loop counters, never an initial teleport.
    // Use the original collision rules directly, without manual TryMove's
    // story guard, step statistics, or arrival-trigger bookkeeping.
    void IEventWorld.BeginScriptedStep(Direction direction)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        var from = _position;
        var target = from.Step(direction);
        _facing = direction; _picture = null;
        _walkFrameOffset = _walkFrameOffset % 12 + 2;
        if (SceneMovementRules.Check(Scene, from, target) == MovementBlock.None)
        {
            _position = target;
            ResetCameraAfterMovement();
        }
        ScriptedMotion = new(from, _position, 0);
        PresentationRevision++;
    }

    void IEventWorld.UpdateScriptedMotion(double progress)
    {
        if (!double.IsFinite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        if (ScriptedMotion is { } motion)
        {
            ScriptedMotion = motion with { Progress = Math.Clamp(progress, 0, 1) };
            PresentationRevision++;
        }
    }

    void IEventWorld.EndScriptedWalk()
    {
        ScriptedMotion = null; _walkFrameOffset = 0; _picture = null;
        PresentationRevision++;
    }

    void IEventWorld.CancelScriptedMotion()
    {
        ScriptedMotion = null;
        PresentationRevision++;
    }

    void IEventWorld.SetPictureWrapped(int eventIndex, int picture)
    {
        var word = unchecked((short)picture);
        if (eventIndex == -1) { _picture = word; PresentationRevision++; }
        else Scene.SetEventPicture(eventIndex, word);
    }

    internal void ValidatePresentationCommand(EventCommand command)
    {
        if (command.Operation == EventOperation.AnimatePair)
            foreach (var index in new[] { command.Arguments[0], command.Arguments[3] })
                if (index < -1 || index >= Scene.Events.Count) throw new ArgumentException("Unknown paired animation event.");
    }

    internal void ResetCameraAfterMovement()
    {
        if (Camera is null) return;
        Camera = null;
        PresentationRevision++;
    }

    internal void ResetPresentationForTravel()
    {
        Camera = null; ScriptedMotion = null;
        PresentationRevision++;
    }
}
