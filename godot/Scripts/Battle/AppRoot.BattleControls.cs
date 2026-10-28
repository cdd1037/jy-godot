using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class AppRoot
{
    private void RebuildBattleControls(BattleSession battle)
    {
        foreach (var child in _battleControls.GetChildren()) { _battleControls.RemoveChild(child); child.QueueFree(); }
        _shownBattleActor = battle.CurrentActor; _shownBattlePhase = battle.Phase;
        _shownBattleRound = battle.Round;
        _shownBattlePresenting = battle.IsPresenting;
        _shownBattleConfirmation = battle.Presentation is { WaitingForConfirmation: true } frame ? frame.Token : null;
        _battleActionHint = null;
        SetBattleInputMode(BattleInputMode.None);
        _battleUiError = null;
        _world.SelectedBattleCell = battle.CurrentActor?.Position;
        _battleStateLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _battleControls.AddChild(_battleStateLabel);
        var expectedActor = battle.CurrentActor;
        var expectedPhase = battle.Phase;
        var expectedRound = battle.Round;
        var expectedPresentation = battle.Presentation?.Token;
        Button Action(string text, Action action)
        {
            var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new(0, 34) };
            button.Pressed += () =>
            {
                if (!ReferenceEquals(_world.Session?.Battle, battle) || battle.Phase != expectedPhase || battle.Round != expectedRound ||
                    !ReferenceEquals(battle.CurrentActor, expectedActor) || battle.Presentation?.Token != expectedPresentation) return;
                action();
            };
            _battleControls.AddChild(button); return button;
        }
        if (battle.IsPresenting)
        {
            if (_shownBattleConfirmation is { } token)
                Action("继续", () => ConfirmBattlePresentation(battle, token));
            else _battleControls.AddChild(new Label { Text = "正在行动……" });
        }
        else if (battle.Phase == BattlePhase.PartySelection)
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
            Action("返回场景，继续剧情", () => ShowBattleResults(battle));
        else if (battle.CurrentActor is { IsFriendly: true })
        {
            Action("移动：点击可到达的格子", () => SetBattleInputMode(BattleInputMode.Move)).Disabled = battle.ReachableMoves().Count <= 1;
            var magic = new OptionButton { Name = "BattleMagic", FocusMode = FocusModeEnum.None };
            var slots = battle.AvailableMagicSlots().ToArray();
            foreach (var slot in slots)
            {
                var id = battle.CurrentActor.Role[JinYong.Core.Session.RoleWords.MagicIdBegin + slot];
                magic.AddItem(_world.Session!.BattleResources!.Magics[id].Name, slot);
            }
            _battleMagicSlot = slots.FirstOrDefault(-1);
            magic.ItemSelected += index =>
            {
                if (!ReferenceEquals(_world.Session?.Battle, battle) || !ReferenceEquals(battle.CurrentActor, expectedActor) || battle.Round != expectedRound || battle.IsPresenting) return;
                _battleMagicSlot = magic.GetItemId((int)index); RefreshBattleTargeting();
            };
            _battleControls.AddChild(magic);
            Action("攻击：点击目标格子", () => SetBattleInputMode(BattleInputMode.Attack)).Disabled = slots.Length == 0;
            void Support(string text, BattleInputMode mode, BattleSupportAction action)
            {
                var button = Action(text, () =>
                {
                    SetBattleInputMode(mode);
                    _world.QueueRedraw();
                });
                button.Disabled = !battle.CanUseSupport(action);
            }
            Support("用毒：点击敌人", BattleInputMode.Poison, BattleSupportAction.UsePoison);
            Support("解毒：点击队友或自己", BattleInputMode.Detox, BattleSupportAction.Detox);
            Support("医疗：点击队友或自己", BattleInputMode.Medicine, BattleSupportAction.Medicine);
            BuildBattleItemControls(battle, Action);
            Action("休息", () => RunBattleCommand(current => current.Rest()));
            Action("等待", () => RunBattleCommand(current => current.Wait()));
            _battleActionHint = new Label { Text = "移动后仍可行动。直线武功按所点方向出招。", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _battleControls.AddChild(_battleActionHint);
        }
        else _battleControls.AddChild(new Label { Text = "敌方行动中……" });
    }

    private void BuildBattleItemControls(BattleSession battle, Func<string, Action, Button> action)
    {
        var items = new OptionButton { Name = "BattleItem", FocusMode = FocusModeEnum.None };
        var available = battle.AvailableBattleItems();
        foreach (var id in available) items.AddItem(battleItemName(id), id);
        _battleItemId = available.FirstOrDefault(-1);
        _battleControls.AddChild(items);
        var use = action("使用所选物品", () =>
        {
            if (_battleItemId < 0) return;
            if (_world.Session!.BattleResources!.Items[_battleItemId][ItemWords.ItemType] == 3)
                RunBattleCommand(current => current.UseBattleItem(_battleItemId));
            else
            {
                SetBattleInputMode(BattleInputMode.Throw);
                _world.QueueRedraw();
            }
        });
        use.Name = "UseBattleItem"; use.Disabled = available.Count == 0;
        var expectedActor = battle.CurrentActor;
        var expectedRound = battle.Round;
        items.ItemSelected += index =>
        {
            if (!ReferenceEquals(_world.Session?.Battle, battle) || !ReferenceEquals(battle.CurrentActor, expectedActor) || battle.Round != expectedRound || battle.IsPresenting) return;
            _battleItemId = items.GetItemId((int)index);
            if (_battleInputMode == BattleInputMode.Throw) SetBattleInputMode(BattleInputMode.None);
        };
        string battleItemName(int id) => _world.Session!.BattleResources!.Items[id].Name +
            (_world.Session.Inventory.TryGetValue(id, out var quantity) ? $" × {quantity}" : "");
    }
}
