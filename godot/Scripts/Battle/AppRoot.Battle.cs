using Godot;
using JinYong.Content.Packages;
using JinYong.Core.Battles;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class AppRoot
{
    private VBoxContainer _battleControls = null!;
    private Control[] _explorationControls = [];
    private ScenePackage? _battleReturnView;
    private BattleSession? _shownBattle;
    private BattleCombatant? _shownBattleActor;
    private BattlePhase? _shownBattlePhase;
    private int _shownBattleRound = -1;
    private bool _shownBattlePresenting;
    private long? _shownBattleConfirmation;
    private enum BattleInputMode { None, Move, Attack, Poison, Detox, Medicine, Throw }
    private BattleInputMode _battleInputMode;
    private int _battleMagicSlot;
    private int _battleItemId = -1;
    private double _enemyDelay;
    private Label? _battleStateLabel;
    private Label? _battleActionHint;
    private string? _battleUiError;

    private void BuildBattleUi(VBoxContainer side)
    {
        _battleControls = new VBoxContainer { Name = "BattleControls", Visible = false };
        _battleControls.AddThemeConstantOverride("separation", 9);
        side.AddChild(_battleControls);
        _world.BattleCellSelected += BattleCellSelected;
        _world.BattlePreviewChanged += UpdateBattleTargetHint;
        BuildBattleAudio();
        BuildBattleResultsUi();
    }

    private void RememberExplorationUi(VBoxContainer side) => _explorationControls =
        side.GetChildren().OfType<Control>().Skip(4).ToArray();

    private void AdvanceBattleHost(double delta)
    {
        if (_world.Session is not { } session) return;
        if (session.Battle is not { } battle)
        {
            if (_shownBattle is null) return;
            if (_battleReturnView is { } view)
            {
                _world.LoadPackage(view, session);
                _package = view; _battleReturnView = null;
            }
            _shownBattle = null; _shownBattleActor = null; _shownBattlePhase = null;
            _shownBattlePresenting = false; _shownBattleConfirmation = null;
            _shownBattleRound = -1;
            CloseBattleResults();
            StopBattleAudio();
            _battleControls.Hide();
            foreach (var control in _explorationControls) control.Show();
            SetBattleInputMode(BattleInputMode.None);
            return;
        }
        if (!ReferenceEquals(_shownBattle, battle))
        {
            var library = _package?.Library ?? throw new InvalidOperationException("Battles require the shared resource library.");
            var view = library.OpenBattleView(session);
            _battleReturnView ??= _package;
            _world.LoadPackage(view, session); _package = view;
            _shownBattle = battle; _shownBattleActor = null; _shownBattlePhase = null; _enemyDelay = 0;
            _shownBattlePresenting = false; _shownBattleConfirmation = null;
            CloseBattleResults();
            StopBattleAudio();
            foreach (var control in _explorationControls) control.Hide();
            _battleControls.Show();
            _world.QueueRedraw();
        }
        if (!battle.IsPresenting && battle.CurrentActor is { IsFriendly: false } && battle.Phase == BattlePhase.Acting)
        {
            _enemyDelay += delta;
            if (battle.HasPendingAiContinuation || _enemyDelay >= 0.3)
            {
                _enemyDelay = 0;
                if (!battle.StepEnemy()) _battleUiError = battle.LastError;
                _world.QueueRedraw();
            }
        }
        else _enemyDelay = 0;
        var confirmation = battle.Presentation is { WaitingForConfirmation: true } frame ? (long?)frame.Token : null;
        if (!ReferenceEquals(_shownBattleActor, battle.CurrentActor) || _shownBattlePhase != battle.Phase ||
            _shownBattlePresenting != battle.IsPresenting || _shownBattleConfirmation != confirmation || _shownBattleRound != battle.Round)
            RebuildBattleControls(battle);
        if (battle.Phase == BattlePhase.Finished && !ReferenceEquals(_battleResultsAutoAttempt, battle))
            ShowBattleResults(battle);
        if (_battleStateLabel is not null)
        {
            var actor = battle.CurrentActor;
            var visible = battle.Presentation?.Combatants.FirstOrDefault(person => person.Slot == actor?.Slot);
            _battleStateLabel.Text = actor is null ? "请选择出战人物。" :
                $"第 {battle.Round} 回合 · {(actor.IsFriendly ? "我方" : "敌方")}\n{actor.Name}\n生命 {visible?.Hp ?? actor.Hp}/{visible?.MaximumHp ?? actor.MaximumHp}\n内力 {visible?.Mp ?? actor.Mp} · 体力 {visible?.Words[JinYong.Core.Session.RoleWords.PhysicalPower] ?? actor.PhysicalPower}\n伤势 {visible?.Hurt ?? actor.Role[JinYong.Core.Session.RoleWords.Hurt]} · 中毒 {visible?.Poison ?? actor.Role[JinYong.Core.Session.RoleWords.Poison]}\n剩余移动 {actor.RoundValue}";
            if (battle.Phase == BattlePhase.Finished) _battleStateLabel.Text = battle.Outcome == BattleOutcome.Victory ? "战斗胜利" : "战斗落败";
            if (_battleUiError is not null) _battleStateLabel.Text += "\n" + _battleUiError;
        }
    }
}
