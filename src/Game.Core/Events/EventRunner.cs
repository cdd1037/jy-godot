using JinYong.Core.Maps;

namespace JinYong.Core.Events;

public sealed partial class EventRunner
{
    private readonly StoryCatalog _catalog;
    private readonly IEventWorld _world;
    private readonly List<int> _completed = [];
    private EventProgram? _program;
    private int _cursor, _context;
    private GridPosition _trigger;
    private long _token;
    private uint _random;
    private readonly Stack<(EventProgram Program, int Cursor)> _calls = [];
    private readonly Queue<EventTrace> _trace = [];
    private int _usedItem = -1, _executed;

    public EventRunner(StoryCatalog catalog, IEventWorld world, uint randomSeed = 1)
    {
        _catalog = catalog;
        _world = world;
        _random = randomSeed;
    }
    public bool IsRunning => _program is not null || Pending is not null || PendingLifecycle is not null || PendingShop is not null || PendingEnding is not null;
    public int? ProgramId => _program?.Id;
    public string? Error { get; private set; }
    public int CompletedCount => _completed.Count;
    public IReadOnlyList<int> CompletedPrograms => _completed.AsReadOnly();
    public IReadOnlyList<EventTrace> Trace => _trace.ToArray();

    private void Pump()
    {
        if (IsRetired || Pending is not null || PendingBattle is not null || PendingLifecycle is not null || PendingShop is not null || PendingEnding is not null) return;
        EventCommand? command = null;
        var programId = _program?.Id;
        try
        {
            for (var budget = 0; _program is not null && budget < 10000; budget++)
            {
                if (_shopCommand is not null)
                {
                    command = _shopCommand;
                    if (AdvanceShop()) return;
                }
                if (_tournament is not null)
                {
                    command = _tournament.Origin;
                    if (AdvanceTournament()) return;
                }
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
                    case EventOperation.ExitMusic: _world.SetExitMusicOverride(arguments[0]); break;
                    case EventOperation.Music: _world.PlayMusic(arguments[0]); break;
                    case EventOperation.Shop:
                        _world.BeginShop();
                        _shopCommand = command; _shopPhase = ShopPhase.Greeting;
                        StartDialogue(2974, 111, 0);
                        return;
                    case EventOperation.TournamentTrials:
                        _tournament = new TournamentState(command);
                        if (AdvanceTournament()) return;
                        break;
                    case EventOperation.StatueAnimation:
                        _sceneOperation = command.Operation; _sceneCommand = command;
                        _statuePhase = 0; _statueCounter = 7664;
                        if (NextSceneFrame()) return;
                        break;
                    case EventOperation.Ending:
                        _world.SetPictureWrapped(-1, -86);
                        _sceneOperation = command.Operation; _sceneCommand = command;
                        _pairFirstEvent = arguments[0]; _pairFirstFrame = arguments[1]; _pairFirstEnd = arguments[2];
                        _pairSecondEvent = arguments[3]; _pairSecondFrame = arguments[4];
                        NextSceneFrame();
                        return;
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
                    case EventOperation.Battle:
                        _world.BeginBattle(arguments[0], arguments[3]);
                        _battleCommand = command;
                        PendingBattle = new(++_token, arguments[0], arguments[3]);
                        return;
                    case EventOperation.Death:
                    case EventOperation.LoadMenu:
                        // sub_2E659 reads no talk parameter. Both menus can only
                        // load a replacement game or quit; failed/cancelled host
                        // I/O must retain this request and the frozen old VM.
                        PendingLifecycle = new(++_token, command.Operation == EventOperation.Death
                            ? EventLifecycleKind.Death : EventLifecycleKind.LoadMenu,
                            command.Operation == EventOperation.Death ? arguments[0] : -1);
                        return;
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

    internal int DrawRandom(int bound) => bound <= 1 || bound > 30000 ? 0 : RandomBounded(bound);

    private int RandomBounded(int upperBound)
    {
        _random = unchecked(_random * 0x41C64E6D + 0x3039);
        return (int)(((_random >> 16) & 0x7FFF) % (uint)upperBound);
    }
}
