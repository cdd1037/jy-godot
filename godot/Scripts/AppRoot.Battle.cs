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
    private enum BattleInputMode { None, Move, Attack }
    private BattleInputMode _battleInputMode;
    private int _battleMagicSlot;
    private double _enemyDelay;
    private Label? _battleStateLabel;
    private string? _battleUiError;

    private void BuildBattleUi(VBoxContainer side)
    {
        _battleControls = new VBoxContainer { Name = "BattleControls", Visible = false };
        _battleControls.AddThemeConstantOverride("separation", 9);
        side.AddChild(_battleControls);
        _world.BattleCellSelected += BattleCellSelected;
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
            _battleControls.Hide();
            foreach (var control in _explorationControls) control.Show();
            _world.BattleMovePreview = false;
            return;
        }
        if (!ReferenceEquals(_shownBattle, battle))
        {
            var library = _package?.Library ?? throw new InvalidOperationException("Battles require the shared resource library.");
            var view = library.OpenBattleView(session);
            _battleReturnView ??= _package;
            _world.LoadPackage(view, session); _package = view;
            _shownBattle = battle; _shownBattleActor = null; _shownBattlePhase = null; _enemyDelay = 0;
            foreach (var control in _explorationControls) control.Hide();
            _battleControls.Show();
            _world.QueueRedraw();
        }
        if (battle.CurrentActor is { IsFriendly: false } && battle.Phase == BattlePhase.Acting)
        {
            _enemyDelay += delta;
            if (_enemyDelay >= 0.35)
            {
                _enemyDelay = 0;
                if (!battle.StepEnemy()) _battleUiError = battle.LastError;
                _world.QueueRedraw();
            }
        }
        else _enemyDelay = 0;
        if (!ReferenceEquals(_shownBattleActor, battle.CurrentActor) || _shownBattlePhase != battle.Phase)
            RebuildBattleControls(battle);
        if (_battleStateLabel is not null)
        {
            var actor = battle.CurrentActor;
            _battleStateLabel.Text = actor is null ? "请选择出战人物。" :
                $"第 {battle.Round} 回合 · {(actor.IsFriendly ? "我方" : "敌方")}\n{actor.Name}\n生命 {actor.Hp}/{actor.MaximumHp}\n内力 {actor.Mp} · 体力 {actor.PhysicalPower}\n剩余移动 {actor.RoundValue}";
            if (battle.Phase == BattlePhase.Finished) _battleStateLabel.Text = battle.Outcome == BattleOutcome.Victory ? "战斗胜利" : "战斗落败";
            if (_battleUiError is not null) _battleStateLabel.Text += "\n" + _battleUiError;
        }
    }

    private void RebuildBattleControls(BattleSession battle)
    {
        foreach (var child in _battleControls.GetChildren()) { _battleControls.RemoveChild(child); child.QueueFree(); }
        _shownBattleActor = battle.CurrentActor; _shownBattlePhase = battle.Phase;
        _battleInputMode = BattleInputMode.None; _world.BattleMovePreview = false;
        _battleUiError = null;
        _world.SelectedBattleCell = battle.CurrentActor?.Position;
        _battleStateLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _battleControls.AddChild(_battleStateLabel);
        Button Action(string text, Action action)
        {
            var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new(0, 34) };
            button.Pressed += action; _battleControls.AddChild(button); return button;
        }
        if (battle.Phase == BattlePhase.PartySelection)
        {
            var options = new List<(int Id, CheckBox Box)>();
            foreach (var id in battle.SelectableRoleIds)
            {
                var mandatory = battle.MandatoryRoleIds.Contains(id);
                var box = new CheckBox { Text = _world.Session!.Party!.Roles[id].Name + (mandatory ? "（必选）" : ""),
                    ButtonPressed = true, Disabled = mandatory, FocusMode = FocusModeEnum.None };
                options.Add((id, box)); _battleControls.AddChild(box);
            }
            Action("确认出战", () => RunBattleCommand(current => current.ConfirmParty(options.Where(option => option.Box.ButtonPressed).Select(option => option.Id).ToArray())));
        }
        else if (battle.Phase == BattlePhase.Finished)
            Action("返回场景，继续剧情", () =>
            {
                try
                {
                    if (!_world.Session!.FinishBattle()) _battleUiError = "战斗结果尚未就绪。";
                    AdvanceBattleHost(0); BindStory(); _world.QueueRedraw();
                }
                catch (Exception error) { _battleUiError = "结算失败：" + error.Message; GD.PushError(error.ToString()); }
            });
        else if (battle.CurrentActor is { IsFriendly: true })
        {
            Action("移动：点击可到达的格子", () => { _battleInputMode = BattleInputMode.Move; _world.BattleMovePreview = true; _world.QueueRedraw(); });
            var magic = new OptionButton { Name = "BattleMagic", FocusMode = FocusModeEnum.None };
            var slots = battle.AvailableMagicSlots().ToArray();
            foreach (var slot in slots)
            {
                var id = battle.CurrentActor.Role[JinYong.Core.Session.RoleWords.MagicIdBegin + slot];
                magic.AddItem(_world.Session!.BattleResources!.Magics[id].Name, slot);
            }
            _battleMagicSlot = slots.FirstOrDefault(-1);
            magic.ItemSelected += index => _battleMagicSlot = magic.GetItemId((int)index);
            _battleControls.AddChild(magic);
            Action("攻击：点击目标格子", () => { _battleInputMode = BattleInputMode.Attack; _world.BattleMovePreview = false; _world.QueueRedraw(); }).Disabled = slots.Length == 0;
            Action("休息", () => RunBattleCommand(current => current.Rest()));
            Action("等待", () => RunBattleCommand(current => current.Wait()));
            _battleControls.AddChild(new Label { Text = "移动后仍可攻击。\n直线武功按所点方向出招。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }
        else _battleControls.AddChild(new Label { Text = "敌方行动中……" });
    }

    private void RunBattleCommand(Func<BattleSession, bool> action)
    {
        if (_world.Session?.Battle is not { } battle) return;
        try
        {
            var success = action(battle);
            if (_battleSmoke && success)
            {
                if (_battleInputMode == BattleInputMode.Move) _battleMoves++;
                if (_battleInputMode == BattleInputMode.Attack) _battleAttacks++;
            }
            _battleUiError = success ? null : "无法执行：" + battle.LastError;
            if (success) { _battleInputMode = BattleInputMode.None; _world.BattleMovePreview = false; }
        }
        catch (Exception error) { _battleUiError = error.Message; GD.PushError(error.ToString()); }
        _world.QueueRedraw();
    }

    private void BattleCellSelected(GridPosition cell)
    {
        if (_world.Session?.Battle is not { CurrentActor.IsFriendly: true } battle) return;
        if (_battleInputMode == BattleInputMode.Move) RunBattleCommand(current => current.TryMove(cell));
        else if (_battleInputMode == BattleInputMode.Attack)
        {
            var direction = BattleMagicRules.Facing(battle.CurrentActor!.Position, cell);
            RunBattleCommand(current => current.AttackMagic(_battleMagicSlot, cell, direction));
        }
    }
}
