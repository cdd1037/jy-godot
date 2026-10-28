using Godot;
using JinYong.Content.Packages;
using JinYong.Core.Battles;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Coordinates battle views and delegates all authoritative decisions to GameSession/BattleSession.</summary>
public sealed class BattleController : IDisposable
{
    private readonly GameViewHost _host;
    private readonly BattleAudioPlayer _audio;
    private readonly BattleCommandRouter _commands;
    private ScenePackage? _returnView;
    private BattleSession? _shownBattle, _resultsBattle, _resultsAttempt;
    private GameSession? _resultsSession;
    private PanelState? _panelState;
    private string? _error;
    private double _enemyDelay;
    private bool _confirming, _disposed;
    private ulong? _lastResultConfirmationFrame;
    private sealed record PanelState(BattleSession Battle, BattleCombatant? Actor, BattlePhase Phase,
        int Round, bool Presenting, long? Confirmation);

    public BattleActionPanel Actions { get; }
    public BattleResultsPanel Results { get; }
    public bool ResultsOpen => Results.IsOpen;
    public BattleTargetingAction SelectionMode => _commands.SelectionMode;
    public IReadOnlyList<string> PlayedSounds => _audio.PlayedSounds;
    public event Action<bool>? BattleModeChanged;
    public event Action? InputAcknowledged;
    public event Action? ResultConfirmed;
    public event Action<BattleActionKind>? ActionExecuted
    {
        add => _commands.ActionExecuted += value;
        remove => _commands.ActionExecuted -= value;
    }

    public BattleController(GameViewHost host, BattleActionPanel actions, BattleResultsPanel results, BattleAudioPlayer audio)
    {
        _host = host; Actions = actions; Results = results; _audio = audio;
        _commands = new(host, actions);
        _commands.PresentationConfirmed += PresentationConfirmed;
        _commands.ResultsRequested += ShowResults;
        host.SessionChanged += Reset;
        results.ContinueRequested += ConfirmResults;
        audio.PlaybackFailed += AudioFailed;
        actions.Hide();
    }

    public void Advance(double delta)
    {
        if (_disposed || _host.Session is not { } session) return;
        if (session.Battle is not { } battle)
        {
            if (_shownBattle is null) return;
            if (_returnView is { } view) _host.Load(view, session);
            ClearState(releaseAudio: false); BattleModeChanged?.Invoke(false); return;
        }
        if (!ReferenceEquals(_shownBattle, battle))
        {
            var library = _host.Library ?? throw new InvalidOperationException("Battles require the shared resource library.");
            var view = library.OpenBattleView(session);
            _returnView ??= _host.Package;
            _host.Load(view, session);
            _shownBattle = battle; _panelState = null; _enemyDelay = 0; _error = null;
            ClearResults(); _audio.Stop(); _commands.Reset();
            Actions.Show(); BattleModeChanged?.Invoke(true); _host.View.QueueRedraw();
        }
        if (!battle.IsPresenting && battle.CurrentActor is { IsFriendly: false } && battle.Phase == BattlePhase.Acting)
        {
            _enemyDelay += delta;
            if (battle.HasPendingAiContinuation || _enemyDelay >= 0.3)
            {
                _enemyDelay = 0;
                if (!battle.StepEnemy()) _error = battle.LastError;
                _host.View.QueueRedraw();
            }
        }
        else _enemyDelay = 0;
        var confirmation = battle.Presentation is { WaitingForConfirmation: true } frame ? (long?)frame.Token : null;
        var state = new PanelState(battle, battle.CurrentActor, battle.Phase, battle.Round, battle.IsPresenting, confirmation);
        if (_panelState != state)
        {
            _panelState = state; _error = null;
            var model = BattleActionModelFactory.Build(session, battle, new BattleActionToken());
            _commands.Bind(model, battle); Actions.Render(model);
            _host.View.SelectedBattleCell = battle.CurrentActor?.Position;
        }
        if (battle.Phase == BattlePhase.Finished && !ReferenceEquals(_resultsAttempt, battle)) ShowResults(battle);
        if (_host.Library is { } activeLibrary) _audio.Play(activeLibrary, battle.TakePresentationSounds());
        Actions.UpdateStatus(BattleActionModelFactory.Status(battle, _error ?? _commands.Error));
    }

    public bool HandleKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return false;
        if (ResultsOpen)
        {
            if (key.Keycode is Key.Enter or Key.KpEnter or Key.Space or Key.E) ConfirmResults();
            return true;
        }
        if (_commands.ConfirmPresentation()) return true;
        if (key.Keycode == Key.Escape && SelectionMode != BattleTargetingAction.None)
        { _commands.CancelSelection(); return true; }
        return false;
    }

    private void PresentationConfirmed()
    {
        Advance(0); InputAcknowledged?.Invoke();
    }

    private void ShowResults(BattleSession battle)
    {
        if (_host.Session is not { } session || !ReferenceEquals(session.Battle, battle) ||
            battle.Phase != BattlePhase.Finished || battle.IsPresenting) return;
        _resultsAttempt = battle;
        try
        {
            if (!session.PrepareBattleSettlement() || session.PendingBattleSettlement is not { } result)
            { _error = session.BattleSettlementError ?? "战果尚未就绪，请稍后重试。"; return; }
            if (ReferenceEquals(_resultsBattle, battle) && ReferenceEquals(_resultsSession, session) && ResultsOpen) return;
            var model = BattleResultsFormatter.BuildModel(session, result, battle.Definition.Name,
                battle.Outcome == BattleOutcome.Victory, battle.Combatants.Any(actor => actor.IsFriendly && !actor.Hidden));
            _resultsBattle = battle; _resultsSession = session;
            _commands.Reset(); _host.View.ReadMovement([], false); Results.ShowResults(model);
        }
        catch (Exception error) { _error = "战果显示失败：" + error.Message; GD.PushError(error.ToString()); }
    }

    private void ConfirmResults()
    {
        if (_confirming || _lastResultConfirmationFrame == Engine.GetProcessFrames() || !ResultsOpen || _resultsSession is not { } session ||
            !ReferenceEquals(_host.Session, session) || !ReferenceEquals(session.Battle, _resultsBattle)) return;
        _confirming = true; Results.SetConfirming(true);
        try
        {
            if (!session.FinishBattle()) { Results.ShowError("战斗结果尚未就绪，请稍后重试。"); return; }
            _lastResultConfirmationFrame = Engine.GetProcessFrames();
            ClearResults(); Advance(0);
            ResultConfirmed?.Invoke(); InputAcknowledged?.Invoke(); _host.View.QueueRedraw();
        }
        catch (Exception error) { Results.ShowError("无法继续剧情：" + error.Message); GD.PushError(error.ToString()); }
        finally { _confirming = false; Results.SetConfirming(false); }
    }

    private void AudioFailed(string message) => _error = message;
    private void ClearResults()
    {
        Results.Close(); _resultsBattle = _resultsAttempt = null; _resultsSession = null;
    }
    public void Reset()
    {
        if (_disposed) return;
        ClearState(releaseAudio: true); BattleModeChanged?.Invoke(false);
    }
    private void ClearState(bool releaseAudio)
    {
        _returnView = null; _shownBattle = null; _panelState = null; _enemyDelay = 0; _error = null;
        ClearResults(); _commands.Reset(); Actions.Hide(); _audio.Stop(releaseAudio);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.SessionChanged -= Reset;
        Results.ContinueRequested -= ConfirmResults;
        _audio.PlaybackFailed -= AudioFailed;
        _commands.PresentationConfirmed -= PresentationConfirmed;
        _commands.ResultsRequested -= ShowResults;
        _commands.Dispose();
    }
}
