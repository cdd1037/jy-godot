using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class AppRoot
{
    private void RunBattleCommand(Func<BattleSession, bool> action)
    {
        if (BattleResultsVisible || _world.Session?.Battle is not { IsPresenting: false } battle ||
            battle.Phase != BattlePhase.PartySelection && battle.CurrentActor is not { IsFriendly: true }) return;
        try
        {
            var success = action(battle);
            if (_battleSmoke && success)
            {
                if (_battleInputMode == BattleInputMode.Move) _battleMoves++;
                if (_battleInputMode == BattleInputMode.Attack) _battleAttacks++;
            }
            _battleUiError = success ? null : "无法执行：" + battle.LastError;
            if (success) SetBattleInputMode(BattleInputMode.None);
        }
        catch (Exception error) { _battleUiError = error.Message; GD.PushError(error.ToString()); }
        _world.QueueRedraw();
    }

    private void ConfirmBattlePresentation(BattleSession battle, long token)
    {
        if (!ReferenceEquals(_world.Session?.Battle, battle) || !battle.ConfirmPresentation(token)) return;
        _heldDirections.Clear();
        _modalClosedFrame = Engine.GetProcessFrames();
        AdvanceBattleHost(0); _world.QueueRedraw();
    }

    private void BattleCellSelected(GridPosition cell)
    {
        if (BattleResultsVisible || _world.Session?.Battle is not { IsPresenting: false, CurrentActor.IsFriendly: true } battle) return;
        if (_world.BattlePreview is { IsLegal: false } preview && preview.HoveredCell == cell)
        {
            _battleUiError = preview.Message;
            return;
        }
        if (_battleInputMode == BattleInputMode.Move) RunBattleCommand(current => current.TryMove(cell));
        else if (_battleInputMode == BattleInputMode.Attack)
        {
            var direction = BattleMagicRules.Facing(battle.CurrentActor!.Position, cell);
            RunBattleCommand(current => current.AttackMagic(_battleMagicSlot, cell, direction));
        }
        else if (_battleInputMode == BattleInputMode.Poison) RunBattleCommand(current => current.UsePoison(cell));
        else if (_battleInputMode == BattleInputMode.Detox) RunBattleCommand(current => current.Detox(cell));
        else if (_battleInputMode == BattleInputMode.Medicine) RunBattleCommand(current => current.Medicine(cell));
        else if (_battleInputMode == BattleInputMode.Throw) RunBattleCommand(current => current.ThrowItem(_battleItemId, cell));
    }

    private void SetBattleInputMode(BattleInputMode mode)
    {
        if (mode != BattleInputMode.None) _battleUiError = null;
        _battleInputMode = mode;
        _world.BattleMovePreview = mode == BattleInputMode.Move;
        RefreshBattleTargeting();
        _world.QueueRedraw();
    }

    private void RefreshBattleTargeting()
    {
        _world.BattleTargeting = _battleInputMode switch
        {
            BattleInputMode.Move => new(BattleTargetingAction.Move),
            BattleInputMode.Attack => new(BattleTargetingAction.Magic, MagicSlot: _battleMagicSlot),
            BattleInputMode.Poison => new(BattleTargetingAction.UsePoison),
            BattleInputMode.Detox => new(BattleTargetingAction.Detox),
            BattleInputMode.Medicine => new(BattleTargetingAction.Medicine),
            BattleInputMode.Throw => new(BattleTargetingAction.ThrowItem, ItemId: _battleItemId),
            _ => null
        };
        UpdateBattleTargetHint(_world.BattlePreview);
    }

    private void UpdateBattleTargetHint(BattleTargetingPreview? preview)
    {
        if (_battleActionHint is null) return;
        if (preview is null)
        {
            _battleActionHint.Text = "移动后仍可行动。直线武功按所点方向出招。";
            return;
        }
        _battleActionHint.Text = preview.Message;
        foreach (var target in preview.Targets)
            _battleActionHint.Text += $"\n{(target.IsFriendly ? "我方" : "敌方")} {target.Name} · 生命 {target.Hp}/{target.MaximumHp} · 内力 {target.Mp}/{target.MaximumMp}";
        _battleActionHint.Text += preview.Request.Action == BattleTargetingAction.Move
            ? "\n蓝色为可达格，青色为移动路径。" : "\n蓝色为可选格，橙色为影响范围。";
        _battleActionHint.Text += "\nEsc 取消选取。";
    }
}
