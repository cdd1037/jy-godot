using JinYong.Core.Maps;

namespace JinYong.Core.Events;

public interface IEventWorld
{
    void ChangeEvent(int context, GridPosition trigger, IReadOnlyList<int> arguments);
    void Face(Direction direction);
    void SetPicture(int eventIndex, int picture);
    void AddItem(int id, int count);
    int ItemCount(int id);
    int GetVariable(int id);
    void SetVariable(int id, int value);
    int EventField(int context, IReadOnlyList<int> arguments);
    void SetTile(IReadOnlyList<int> arguments);
    void Teleport(GridPosition position);
    void ValidateCommand(EventCommand command, int context);
    bool ItemExists(int id) => ItemCount(id) > 0;
    bool TestGameCondition(EventCommand command) => throw new InvalidOperationException("Game state service is unavailable.");
    string? ExecuteGameCommand(EventCommand command, int context, GridPosition trigger, int randomValue) =>
        throw new InvalidOperationException("Game state service is unavailable.");
    void SetFade(double opacity) => throw new InvalidOperationException("Fade presentation service is unavailable.");
    void SetCameraAxis(bool horizontal, int coordinate) => throw new InvalidOperationException("Scene camera service is unavailable.");
    void BeginScriptedStep(Direction direction) => throw new InvalidOperationException("Scripted movement service is unavailable.");
    void UpdateScriptedMotion(double progress) => throw new InvalidOperationException("Scripted movement service is unavailable.");
    void EndScriptedWalk() => throw new InvalidOperationException("Scripted movement service is unavailable.");
    void CancelScriptedMotion() { }
    void SetPictureWrapped(int eventIndex, int picture) => SetPicture(eventIndex, unchecked((short)picture));
}

public enum EventPresentationKind { Dialogue, Item, Error, Choice, Notice }
public sealed record EventPresentation(long Token, EventPresentationKind Kind, int DialogueId,
    int PortraitId, int Style, string Text, int Page, int PageCount, IReadOnlyList<EventChoice>? Choices = null, bool CanCancel = false);
public sealed record EventTrace(int Program, int Command, EventOperation Operation);

public sealed class EventRunner
{
    // OpenLegend's picture animation waits two BIOS ticks for each even frame.
    public static readonly TimeSpan AnimationFrameTime = TimeSpan.FromSeconds(2.0 * 65536 / 1193182);
    public static readonly TimeSpan ScriptedWalkFrameTime = TimeSpan.FromSeconds(3.0 * 65536 / 1193182);
    private readonly StoryCatalog _catalog;
    private readonly IEventWorld _world;
    private readonly Queue<int> _sounds = [];
    private readonly List<int> _completed = [];
    private EventProgram? _program;
    private int _cursor, _context, _animationEvent, _nextFrame, _endFrame;
    private GridPosition _trigger;
    private bool _animating, _refresh;
    private TimeSpan _animationWait;
    private long _token;
    private uint _random;
    private DialogueDefinition? _dialogue;
    private int _page, _portrait, _style;
    private readonly Stack<(EventProgram Program, int Cursor)> _calls = [];
    private readonly Queue<EventTrace> _trace = [];
    private TimeSpan? _wait;
    private int _usedItem = -1, _cancelTarget = -1, _executed;
    private bool _fading;
    private int _fadeTarget;
    private TimeSpan _fadeElapsed, _fadeDuration;
    private EventCommand? _fadeCommand;
    private EventOperation? _sceneOperation;
    private EventCommand? _sceneCommand;
    private int _sceneX, _sceneY, _targetX, _targetY;
    private int _pairFirstEvent, _pairFirstFrame, _pairFirstEnd, _pairSecondEvent, _pairSecondFrame;
    private TimeSpan _sceneWait;

    public EventRunner(StoryCatalog catalog, IEventWorld world, uint randomSeed = 1)
    {
        _catalog = catalog;
        _world = world;
        _random = randomSeed;
    }
    public bool IsRunning => _program is not null || Pending is not null;
    public int? ProgramId => _program?.Id;
    public EventPresentation? Pending { get; private set; }
    public string? Error { get; private set; }
    public int CompletedCount => _completed.Count;
    public IReadOnlyList<int> CompletedPrograms => _completed.AsReadOnly();
    public IReadOnlyList<EventTrace> Trace => _trace.ToArray();

    public bool TryStart(int programId, int eventContext, GridPosition trigger, int usedItem = -1)
    {
        if (IsRunning || !_catalog.Programs.TryGetValue(programId, out var program)) return false;
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

    public bool Confirm(long token)
    {
        if (Pending is not { } pending || pending.Token != token) return false;
        if (pending.Kind == EventPresentationKind.Choice) return false;
        if (pending.Kind == EventPresentationKind.Error) { Pending = null; return true; }
        if (_dialogue is not null && ++_page < _dialogue.Pages.Count) { ShowPage(); return true; }
        _dialogue = null;
        Pending = null;
        Pump();
        return true;
    }

    public bool Choose(long token, int option)
    {
        if (Pending is not { Kind: EventPresentationKind.Choice, Choices: { } choices } pending || pending.Token != token ||
            option < 0 || option >= choices.Count) return false;
        _cursor = _program!.IndexOf(choices[option].Target);
        Pending = null;
        Pump();
        return true;
    }

    public bool CancelChoice(long token)
    {
        if (Pending is not { Kind: EventPresentationKind.Choice, CanCancel: true } pending || pending.Token != token) return false;
        _cursor = _program!.IndexOf(_cancelTarget);
        Pending = null;
        Pump();
        return true;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (Pending is not null || _program is null) return;
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

    public int[] TakeSounds()
    {
        var result = _sounds.ToArray();
        _sounds.Clear();
        return result;
    }

    private void ShowPage()
    {
        var dialogue = _dialogue!;
        Pending = new(++_token, EventPresentationKind.Dialogue, dialogue.Id, _portrait, _style,
            dialogue.Pages[_page], _page + 1, dialogue.Pages.Count);
    }

    private void StartDialogue(int id, int portrait, int style)
    {
        _dialogue = _catalog.Dialogues.TryGetValue(id, out var dialogue) ? dialogue
            : throw new InvalidOperationException($"Missing dialogue {id}.");
        _page = 0; _portrait = portrait; _style = style;
        ShowPage();
    }

    private void Pump()
    {
        EventCommand? command = null;
        var programId = _program?.Id;
        try
        {
            for (var budget = 0; _program is not null && budget < 10000; budget++)
            {
                if (_cursor >= _program.Commands.Count) throw new InvalidOperationException("Program has no end command.");
                command = _program.Commands[_cursor++];
                if (++_executed > 100000) throw new InvalidOperationException("Event exceeded its total execution budget.");
                _trace.Enqueue(new(_program.Id, command.Id, command.Operation));
                if (_trace.Count > 64) _trace.Dequeue();
                var arguments = command.Arguments;
                switch (command.Operation)
                {
                    case EventOperation.Refresh: _refresh = true; return;
                    case EventOperation.Dialogue: StartDialogue(arguments[0], arguments[1], arguments[2]); return;
                    case EventOperation.ChangeEvent: _world.ChangeEvent(_context, _trigger, arguments); break;
                    case EventOperation.Face: _world.Face((Direction)Math.Clamp(arguments[0], 0, 3)); break;
                    case EventOperation.Sound: _sounds.Enqueue(arguments[0]); break;
                    case EventOperation.AddItem:
                        _world.AddItem(arguments[0], arguments[1]);
                        Pending = new(++_token, EventPresentationKind.Item, -1, -1, 1,
                            $"{(arguments[1] >= 0 ? "获得" : "消耗")} {_catalog.ItemNames[arguments[0]]} × {Math.Abs((long)arguments[1])}", 1, 1);
                        return;
                    case EventOperation.Animate:
                        _animationEvent = arguments[0]; _nextFrame = arguments[1]; _endFrame = arguments[2];
                        if (_nextFrame > _endFrame || (_nextFrame & 1) != 0 || (_endFrame & 1) != 0)
                            throw new InvalidOperationException("Invalid animation frame range.");
                        _world.SetPicture(_animationEvent, _nextFrame);
                        _nextFrame += 2;
                        _animating = true;
                        _animationWait = AnimationFrameTime;
                        return;
                    case EventOperation.RandomDialogue:
                        var offset = RandomBounded(arguments[1]);
                        StartDialogue(arguments[0] + offset, arguments[2], arguments[3]);
                        return;
                    case EventOperation.AdjustItem: _world.AddItem(arguments[0], arguments[1]); break;
                    case EventOperation.SetVariable: _world.SetVariable(arguments[0], arguments[1]); break;
                    case EventOperation.AddVariable:
                        _world.SetVariable(arguments[0], checked(_world.GetVariable(arguments[0]) + arguments[1])); break;
                    case EventOperation.Jump: _cursor = _program.IndexOf(arguments[0]); break;
                    case EventOperation.BranchItem:
                        Branch(_world.ItemCount(arguments[0]) >= arguments[1], arguments); break;
                    case EventOperation.BranchItemExists: Branch(_world.ItemExists(arguments[0]), arguments); break;
                    case EventOperation.BranchUsedItem: Branch(_usedItem == arguments[0], arguments); break;
                    case EventOperation.BranchAllItems: Branch(arguments.Take(5).All(_world.ItemExists), arguments); break;
                    case EventOperation.BranchRole:
                    case EventOperation.BranchParty:
                    case EventOperation.BranchPartyFull:
                    case EventOperation.BranchPartyFemale:
                    case EventOperation.BranchBooks: Branch(_world.TestGameCondition(command), arguments); break;
                    case EventOperation.BranchVariable:
                        var left = _world.GetVariable(arguments[0]);
                        var right = arguments[2];
                        var matches = (EventComparison)arguments[1] switch
                        {
                            EventComparison.Equal => left == right, EventComparison.NotEqual => left != right,
                            EventComparison.Less => left < right, EventComparison.LessOrEqual => left <= right,
                            EventComparison.Greater => left > right, EventComparison.GreaterOrEqual => left >= right,
                            _ => throw new InvalidOperationException("Invalid comparison.")
                        };
                        Branch(matches, arguments); break;
                    case EventOperation.BranchEvent: Branch(_world.EventField(_context, arguments) == arguments[3], arguments); break;
                    case EventOperation.Choice:
                        _cancelTarget = arguments[0];
                        Pending = new(++_token, EventPresentationKind.Choice, -1, -1, 0, command.Text!, 1, 1,
                            command.Choices, _cancelTarget != -1);
                        return;
                    case EventOperation.Wait: _wait = TimeSpan.FromMilliseconds(arguments[0]); return;
                    case EventOperation.PanCamera:
                    case EventOperation.ScriptedWalk:
                        _sceneOperation = command.Operation; _sceneCommand = command;
                        _sceneX = arguments[0]; _sceneY = arguments[1]; _targetX = arguments[2]; _targetY = arguments[3];
                        if (NextSceneFrame() || _refresh) return;
                        break;
                    case EventOperation.AnimatePair:
                        _sceneOperation = command.Operation; _sceneCommand = command;
                        _pairFirstEvent = arguments[0]; _pairFirstFrame = arguments[1]; _pairFirstEnd = arguments[2];
                        _pairSecondEvent = arguments[3]; _pairSecondFrame = arguments[4];
                        if (NextSceneFrame()) return;
                        break;
                    case EventOperation.Fade:
                        _fadeTarget = arguments[0];
                        _fadeDuration = TimeSpan.FromMilliseconds(arguments[1]);
                        _fadeElapsed = TimeSpan.Zero;
                        _fadeCommand = command;
                        _world.SetFade(1 - _fadeTarget);
                        if (_fadeDuration == TimeSpan.Zero)
                        {
                            _world.SetFade(_fadeTarget);
                            _fadeCommand = null;
                            break;
                        }
                        _fading = true;
                        return;
                    case EventOperation.SetTile: _world.SetTile(arguments); break;
                    case EventOperation.Teleport: _world.Teleport(new(arguments[0], arguments[1])); break;
                    case EventOperation.JoinParty:
                    case EventOperation.LeaveParty:
                    case EventOperation.RestParty:
                    case EventOperation.ClearPartyMp:
                    case EventOperation.SetRoleAttribute:
                    case EventOperation.AddRoleAttribute:
                    case EventOperation.LearnMagic:
                    case EventOperation.SetMagic:
                    case EventOperation.RoleAddItem:
                    case EventOperation.NoticeRole:
                    case EventOperation.DismissParty:
                    case EventOperation.AddFame:
                    case EventOperation.IncrementEventScripts:
                    case EventOperation.ReplaceTiles:
                    case EventOperation.OpenScene:
                    case EventOperation.OpenAllScenes:
                    case EventOperation.MoveShop:
                        var notice = _world.ExecuteGameCommand(command, _context, _trigger,
                            command.Operation == EventOperation.MoveShop ? RandomBounded(5) : 0);
                        if (notice is not null)
                        {
                            Pending = new(++_token, EventPresentationKind.Notice, -1, -1, 1, notice, 1, 1);
                            return;
                        }
                        break;
                    case EventOperation.Call:
                        if (_calls.Count >= 32) throw new InvalidOperationException("Event call depth exceeded 32.");
                        _calls.Push((_program, _cursor));
                        _program = _catalog.Programs[arguments[0]];
                        _cursor = 0;
                        break;
                    case EventOperation.Stop:
                        _calls.Clear();
                        goto case EventOperation.End;
                    case EventOperation.Return:
                    case EventOperation.End:
                        _completed.Add(_program.Id);
                        if (_calls.TryPop(out var caller)) { _program = caller.Program; _cursor = caller.Cursor; break; }
                        _program = null;
                        return;
                    default: throw new InvalidOperationException($"Unknown normalized command {command.Operation}.");
                }
            }
            if (_program is not null) throw new InvalidOperationException("Event exceeded its command budget.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException)
        { Fail(error, command, programId); }
    }

    private void ClearTimedState()
    {
        if (_sceneOperation == EventOperation.ScriptedWalk) _world.CancelScriptedMotion();
        _sceneOperation = null; _sceneCommand = null; _sceneWait = TimeSpan.Zero;
        _animating = _refresh = _fading = false;
        _wait = null; _fadeCommand = null;
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
        if (_sceneOperation == EventOperation.AnimatePair)
        {
            if (_pairFirstFrame > _pairFirstEnd) { _sceneOperation = null; _sceneCommand = null; return false; }
            _world.SetPictureWrapped(_pairFirstEvent, _pairFirstFrame);
            _world.SetPictureWrapped(_pairSecondEvent, _pairSecondFrame);
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

    private void Fail(Exception error, EventCommand? command, int? programId)
    {
        Error = $"Program {_program?.Id ?? programId}, command {command?.Id}: {error.Message}";
        _program = null; _dialogue = null; ClearTimedState(); _calls.Clear();
        Pending = new(++_token, EventPresentationKind.Error, -1, -1, 0, "剧情无法继续：" + Error, 1, 1);
    }

    private void Branch(bool matches, IReadOnlyList<int> arguments) =>
        _cursor = _program!.IndexOf(arguments[arguments.Count - (matches ? 2 : 1)]);

    private int RandomBounded(int upperBound)
    {
        _random = unchecked(_random * 0x41C64E6D + 0x3039);
        return (int)(((_random >> 16) & 0x7FFF) % (uint)upperBound);
    }
}
