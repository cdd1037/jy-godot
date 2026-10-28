using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Maps;

namespace JinYong.Host;

/// <summary>Validates UI intent ownership and delegates actions to the current core battle.</summary>
public sealed class BattleCommandRouter : IDisposable
{
    private readonly GameViewHost _host;
    private readonly BattleActionPanel _panel;
    private CommandOwner? _owner, _selection;
    private sealed record CommandOwner(BattleActionToken Token, BattleSession Battle, BattleCombatant? Actor,
        BattlePhase Phase, int Round, long? PresentationToken);
    private int _magicSlot = -1, _itemId = -1;
    public BattleTargetingAction SelectionMode { get; private set; }
    public string? Error { get; private set; }
    public event Action<BattleActionKind>? ActionExecuted;
    public event Action? PresentationConfirmed;
    public event Action<BattleSession>? ResultsRequested;

    public BattleCommandRouter(GameViewHost host, BattleActionPanel panel)
    {
        _host = host; _panel = panel;
        panel.ActionRequested += HandleIntent;
        host.View.BattleCellSelected += SelectCell;
        host.View.BattlePreviewChanged += UpdatePreview;
    }

    public void Bind(BattleActionModel model, BattleSession battle)
    {
        Reset();
        _owner = new(model.Token, battle, battle.CurrentActor, battle.Phase, battle.Round, battle.Presentation?.Token);
        _magicSlot = model.SelectedMagicSlot; _itemId = model.SelectedItemId;
    }

    public void Reset()
    {
        _owner = null; Error = null; _magicSlot = _itemId = -1; ClearSelection();
    }

    public void CancelSelection()
    {
        ClearSelection(); _panel.UpdateTargetHint("已取消目标选取。");
    }

    private bool IsCurrent(CommandOwner context) =>
        ReferenceEquals(_owner, context) && ReferenceEquals(_host.Session?.Battle, context.Battle) && context.Battle.Phase == context.Phase &&
        ReferenceEquals(context.Battle.CurrentActor, context.Actor) && context.Battle.Round == context.Round &&
        context.Battle.Presentation?.Token == context.PresentationToken;

    private bool CanAct(CommandOwner context) => IsCurrent(context) && !context.Battle.IsPresenting &&
        (context.Phase == BattlePhase.PartySelection || context.Phase == BattlePhase.Acting && context.Actor is { IsFriendly: true });

    private void HandleIntent(BattleActionIntent intent)
    {
        if (_owner is not { } context || !ReferenceEquals(context.Token, intent.Token) || !IsCurrent(context)) return;
        if (intent.Kind == BattleActionKind.ConfirmPresentation) { ConfirmPresentation(); return; }
        if (intent.Kind == BattleActionKind.ShowResults)
        {
            if (context.Phase == BattlePhase.Finished && !context.Battle.IsPresenting) ResultsRequested?.Invoke(context.Battle);
            return;
        }
        if (!CanAct(context) || (intent.Kind == BattleActionKind.ConfirmParty) != (context.Phase == BattlePhase.PartySelection)) return;
        switch (intent.Kind)
        {
            case BattleActionKind.ConfirmParty:
                Execute(context, intent.Kind, battle => battle.ConfirmParty(intent.SelectedRoles ?? [])); break;
            case BattleActionKind.SelectMove: SelectMode(context, BattleTargetingAction.Move); break;
            case BattleActionKind.SelectMagic:
                if (context.Battle.AvailableMagicSlots().Contains(intent.Value))
                { _magicSlot = intent.Value; RefreshTargeting(); }
                break;
            case BattleActionKind.SelectAttack:
                if (!context.Battle.AvailableMagicSlots().Contains(intent.Value)) return;
                _magicSlot = intent.Value; SelectMode(context, BattleTargetingAction.Magic); break;
            case BattleActionKind.SelectPoison: SelectMode(context, BattleTargetingAction.UsePoison); break;
            case BattleActionKind.SelectDetox: SelectMode(context, BattleTargetingAction.Detox); break;
            case BattleActionKind.SelectMedicine: SelectMode(context, BattleTargetingAction.Medicine); break;
            case BattleActionKind.SelectItem:
                _itemId = intent.Value;
                if (SelectionMode == BattleTargetingAction.ThrowItem) ClearSelection();
                break;
            case BattleActionKind.UseItem:
                if (!context.Battle.AvailableBattleItems().Contains(intent.Value) || _host.Session?.BattleResources is not { } resources) return;
                _itemId = intent.Value;
                if (resources.Items[_itemId][ItemWords.ItemType] == 3)
                    Execute(context, intent.Kind, battle => battle.UseBattleItem(_itemId));
                else SelectMode(context, BattleTargetingAction.ThrowItem);
                break;
            case BattleActionKind.Rest: Execute(context, intent.Kind, battle => battle.Rest()); break;
            case BattleActionKind.Wait: Execute(context, intent.Kind, battle => battle.Wait()); break;
        }
    }

    public bool ConfirmPresentation()
    {
        if (_host.Session?.Battle is not { Presentation: { WaitingForConfirmation: true } frame } battle ||
            !battle.ConfirmPresentation(frame.Token)) return false;
        PresentationConfirmed?.Invoke(); _host.View.QueueRedraw(); return true;
    }

    private void Execute(CommandOwner context, BattleActionKind kind, Func<BattleSession, bool> action)
    {
        if (!CanAct(context)) return;
        try
        {
            var success = action(context.Battle);
            Error = success ? null : "无法执行：" + context.Battle.LastError;
            if (success) { ClearSelection(); ActionExecuted?.Invoke(kind); }
        }
        catch (Exception error) { Error = error.Message; GD.PushError(error.ToString()); }
        _host.View.QueueRedraw();
    }

    private void SelectCell(GridPosition cell)
    {
        if (_selection is not { } context || !CanAct(context)) { ClearSelection(); return; }
        if (_host.View.BattlePreview is { IsLegal: false } preview && preview.HoveredCell == cell)
        { Error = preview.Message; return; }
        switch (SelectionMode)
        {
            case BattleTargetingAction.Move: Execute(context, BattleActionKind.SelectMove, battle => battle.TryMove(cell)); break;
            case BattleTargetingAction.Magic:
                var direction = BattleMagicRules.Facing(context.Actor!.Position, cell);
                Execute(context, BattleActionKind.SelectAttack, battle => battle.AttackMagic(_magicSlot, cell, direction)); break;
            case BattleTargetingAction.UsePoison: Execute(context, BattleActionKind.SelectPoison, battle => battle.UsePoison(cell)); break;
            case BattleTargetingAction.Detox: Execute(context, BattleActionKind.SelectDetox, battle => battle.Detox(cell)); break;
            case BattleTargetingAction.Medicine: Execute(context, BattleActionKind.SelectMedicine, battle => battle.Medicine(cell)); break;
            case BattleTargetingAction.ThrowItem: Execute(context, BattleActionKind.UseItem, battle => battle.ThrowItem(_itemId, cell)); break;
        }
    }

    private void SelectMode(CommandOwner context, BattleTargetingAction mode)
    {
        Error = null; _selection = context; SelectionMode = mode; RefreshTargeting();
    }

    private void ClearSelection()
    {
        SelectionMode = BattleTargetingAction.None; _selection = null;
        _host.View.BattleTargeting = null; _host.View.BattleMovePreview = false;
        _panel.UpdateTargetHint("移动后仍可行动。直线武功按所点方向出招。");
        _host.View.QueueRedraw();
    }

    private void RefreshTargeting()
    {
        _host.View.BattleTargeting = SelectionMode == BattleTargetingAction.None ? null :
            new(SelectionMode, MagicSlot: _magicSlot, ItemId: _itemId);
        UpdatePreview(_host.View.BattlePreview); _host.View.QueueRedraw();
    }

    private void UpdatePreview(BattleTargetingPreview? preview)
    {
        if (preview is null) { _panel.UpdateTargetHint("移动后仍可行动。直线武功按所点方向出招。"); return; }
        var lines = new List<string> { preview.Message };
        lines.AddRange(preview.Targets.Select(target =>
            $"{(target.IsFriendly ? "我方" : "敌方")} {target.Name} · 生命 {target.Hp}/{target.MaximumHp} · 内力 {target.Mp}/{target.MaximumMp}"));
        lines.Add(preview.Request.Action == BattleTargetingAction.Move ? "蓝色为可达格，青色为移动路径。" : "蓝色为可选格，橙色为影响范围。");
        lines.Add("Esc 取消选取。"); _panel.UpdateTargetHint(string.Join("\n", lines));
    }

    public void Dispose()
    {
        _panel.ActionRequested -= HandleIntent;
        _host.View.BattleCellSelected -= SelectCell;
        _host.View.BattlePreviewChanged -= UpdatePreview;
    }
}
