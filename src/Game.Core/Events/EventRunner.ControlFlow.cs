using JinYong.Core.Maps;

namespace JinYong.Core.Events;

public sealed partial class EventRunner
{
    private EventCommand? _battleCommand;
    public EventBattleRequest? PendingBattle { get; private set; }
    public EventLifecycleRequest? PendingLifecycle { get; private set; }
    public bool IsRetired { get; private set; }

    public bool TryStart(int programId, int eventContext, GridPosition trigger, int usedItem = -1)
    {
        if (IsRetired || IsRunning || !_catalog.Programs.TryGetValue(programId, out var program)) return false;
        Error = null;
        try
        {
            var visited = new HashSet<int>();
            void Check(EventProgram candidate)
            {
                if (!visited.Add(candidate.Id)) return;
                foreach (var command in candidate.Commands)
                {
                    try { _world.ValidateCommand(command, eventContext); }
                    catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
                    { throw new InvalidOperationException($"Program {candidate.Id}, command {command.Id}: {error.Message}", error); }
                    if (command.Operation == EventOperation.Call) Check(_catalog.Programs[command.Arguments[0]]);
                }
            }
            Check(program);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            Error = $"Program {programId} cannot start: {error.Message}";
            return false;
        }
        _program = program;
        _cursor = 0;
        _context = eventContext;
        _trigger = trigger;
        _usedItem = usedItem;
        _calls.Clear(); _trace.Clear(); ClearTimedState(); _executed = 0;
        Pump();
        return true;
    }

    /// <summary>The world owns the completed battle; a confirmation cannot choose its outcome.</summary>
    public bool ResumeBattle(long token)
    {
        if (PendingBattle?.Token != token || _battleCommand is not { } command || !_world.TryGetBattleOutcome(out var victory)) return false;
        _cursor = _program!.IndexOf(command.Arguments[victory ? 1 : 2]);
        PendingBattle = null; _battleCommand = null;
        Pump();
        return true;
    }

    /// <summary>Destroy the old VM only after a replacement session has committed.</summary>
    public void Retire()
    {
        if (IsRetired) return;
        ClearTimedState(); _program = null; _calls.Clear(); Pending = null; _dialogue = null;
        _sounds.Clear(); _trace.Clear(); Error = null; IsRetired = true;
    }

    private void Fail(Exception error, EventCommand? command, int? programId)
    {
        Error = $"Program {_program?.Id ?? programId}, command {command?.Id}: {error.Message}";
        _program = null; _dialogue = null; ClearTimedState(); _calls.Clear();
        Pending = new(++_token, EventPresentationKind.Error, -1, -1, 0, "剧情无法继续：" + Error, 1, 1);
    }

    private void Branch(bool matches, IReadOnlyList<int> arguments) =>
        _cursor = _program!.IndexOf(arguments[arguments.Count - (matches ? 2 : 1)]);
}
