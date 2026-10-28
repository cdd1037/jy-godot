using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Events;
using JinYong.Core.Items;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class AppRoot
{
    private bool _itemActionsSmoke;
    private int _itemActionStage, _itemActionChecks, _actionMedicine, _actionEquipment, _actionBook, _actionThrown;
    private int _actionMedicineCount, _actionEquipmentCount, _actionBookCount, _actionThrownCount;
    private GameSession? _itemActionSession;
    private BattleCombatant? _itemActionActor;
    private int _itemActionMovedRound = -1;

    private void AdvanceItemActionsSmoke()
    {
        if (_battleMouseDown)
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false,
                Position = SmokeMousePosition(_world.GlobalPosition + _world.Size / 2) });
            _battleMouseDown = false;
            return;
        }
        try
        {
            var session = _world.Session!;
            if (_itemActionSession is not null && !ReferenceEquals(_itemActionSession, session))
                throw new InvalidOperationException("Item actions replaced the authoritative session.");
            if (session.Events?.Pending is { } pending)
            {
                if (pending.Kind == EventPresentationKind.Error) throw new InvalidOperationException(pending.Text);
                Input.ActionPress("interact"); _smokeConfirmPressed = true; return;
            }
            if (_itemActionStage == 0)
            {
                if (!session.CanSave || session.BattleResources is null || _package?.Library is null) return;
                if (session.World.CurrentSceneId != 70 || session.Story?.Programs.ContainsKey(11001) != true)
                    throw new InvalidOperationException("Item-actions smoke requires the original library and BattleDemo overlay in scene 70.");
                PrepareItemActionsFixture(session); ShowInventory(); _itemActionStage = 1; return;
            }
            if (_itemActionStage is >= 1 and <= 4)
            {
                if (_inventoryDialog is not { Visible: true } dialog) throw new InvalidOperationException("Expanded inventory did not open.");
                var apply = dialog.GetNode<Button>("InventoryContents/ApplyItemToRole");
                if (_itemActionStage == 4)
                {
                    if (_capturePath is not null && !_inventoryCaptured && DisplayServer.GetName() != "headless")
                    { if (!_capturingInventory) _ = CaptureInventoryAsync(); return; }
                    dialog.EmitSignal(AcceptDialog.SignalName.Confirmed); _itemActionStage = 5; return;
                }
                var id = _itemActionStage == 1 ? _actionMedicine : _itemActionStage == 2 ? _actionEquipment : _actionBook;
                SelectInventorySmokeItem(dialog, id);
                if (apply.Disabled) throw new InvalidOperationException("Inventory role action unexpectedly disabled: " + apply.TooltipText);
                var hp = session.Party!.GetWord(0, RoleWords.Hp);
                apply.EmitSignal(BaseButton.SignalName.Pressed);
                var passed = _itemActionStage switch
                {
                    1 => session.Inventory.GetValueOrDefault(_actionMedicine) == _actionMedicineCount - 1 && session.Party.GetWord(0, RoleWords.Hp) > hp,
                    2 => session.Party.ItemUsers[_actionEquipment] == 0 && session.Inventory[_actionEquipment] == _actionEquipmentCount,
                    _ => session.Party.GetWord(0, RoleWords.PracticeItem) == _actionBook && session.Party.ItemUsers[_actionBook] == 0 &&
                        session.Inventory[_actionBook] == _actionBookCount
                };
                if (!passed) throw new InvalidOperationException("Inventory did not apply medicine/equipment/practice to shared state.");
                _itemActionChecks++; _itemActionStage++; return;
            }
            if (_itemActionStage == 5)
            {
                Input.ActionPress("interact"); _smokeConfirmPressed = true; _itemActionStage = 6; return;
            }
            if (session.Battle is not { } battle)
            {
                if (_itemActionStage < 13 || session.IsEventRunning) return;
                var good = session.Variables.GetValueOrDefault("example:battle/result") == 1 && session.CanSave &&
                    session.Inventory.GetValueOrDefault(_actionMedicine) == _actionMedicineCount - 2 &&
                    session.Inventory.GetValueOrDefault(_actionThrown) == _actionThrownCount - 1 &&
                    session.Party!.GetWord(0, RoleWords.PracticeItem) == _actionBook && _itemActionChecks == 8;
                if (good) _itemActionChecks++;
                _ = FinishSmokeAsync(good, "Explicit prepared fixture: equipped and practiced from inventory, consumed medicine in exploration and battle, used medical/detox/poison targeting and a thrown item through UI, then settled actual battle victory in the same session/backpack.");
                return;
            }
            if (battle.Phase == BattlePhase.PartySelection) { PressBattleButton("确认出战"); return; }
            if (battle.Phase == BattlePhase.Finished)
            {
                if (_itemActionStage != 13 || battle.Outcome != BattleOutcome.Victory)
                    throw new InvalidOperationException("The prepared battle ended before every new action was exercised.");
                PressBattleButton("返回场景，继续剧情"); return;
            }
            if (battle.CurrentActor is not { IsFriendly: true } actor) return;
            if (_capturePath is not null && !_battleCaptured && DisplayServer.GetName() != "headless")
            { if (!_capturingBattle) _ = CaptureBattleAsync(); return; }
            if (_itemActionStage == 6)
            {
                _itemActionActor = actor;
                PressBattleButton("医疗：点击队友或自己"); SendBattleMouse(actor.Position);
                _itemActionStage = 7; return;
            }
            if (_itemActionStage == 7)
            {
                if (battle.LastSupportResult is not { Action: BattleSupportAction.Medicine, Amount: > 0 })
                    throw new InvalidOperationException($"Medical targeting did not produce actual healing: result={battle.LastSupportResult}, mode={_battleInputMode}, selected={_world.SelectedBattleCell}, active={actor.Position}, error={battle.LastError}.");
                _itemActionChecks++;
                PressBattleButton("解毒：点击队友或自己"); SendBattleMouse(actor.Position); _itemActionStage = 8; return;
            }
            if (_itemActionStage == 8)
            {
                if (battle.LastSupportResult is not { Action: BattleSupportAction.Detox, Amount: > 0 } ||
                    _itemActionActor!.Role[RoleWords.Poison] != 0) throw new InvalidOperationException("Detox targeting did not clear the shared actor's poison.");
                _itemActionChecks++;
                SelectBattleSmokeItem(_actionMedicine);
                _battleControls.GetNode<Button>("UseBattleItem").EmitSignal(BaseButton.SignalName.Pressed);
                _itemActionStage = 9; return;
            }
            if (_itemActionStage == 9)
            {
                if (session.Inventory.GetValueOrDefault(_actionMedicine) != _actionMedicineCount - 2)
                    throw new InvalidOperationException("Battle medicine did not consume the same exploration backpack.");
                _itemActionChecks++; _itemActionStage = 10;
            }
            var enemy = battle.Combatants.Where(other => !other.IsFriendly && other.IsAlive)
                .OrderBy(other => Math.Abs(other.Position.X - actor.Position.X) + Math.Abs(other.Position.Y - actor.Position.Y)).First();
            if (_itemActionStage == 10)
            {
                if (!battle.CanTargetSupport(BattleSupportAction.UsePoison, enemy.Position))
                { MoveItemSmokeToward(battle, actor, enemy); return; }
                PressBattleButton("用毒：点击敌人"); SendBattleMouse(enemy.Position); _itemActionStage = 11; return;
            }
            if (_itemActionStage == 11)
            {
                if (battle.LastSupportResult is not { Action: BattleSupportAction.UsePoison, Amount: > 0 })
                    throw new InvalidOperationException("Poison targeting did not change an enemy.");
                _itemActionChecks++; _itemActionStage = 12;
            }
            if (_itemActionStage == 12)
            {
                if (!battle.CanTargetThrownItem(_actionThrown, enemy.Position))
                { MoveItemSmokeToward(battle, actor, enemy); return; }
                SelectBattleSmokeItem(_actionThrown);
                _battleControls.GetNode<Button>("UseBattleItem").EmitSignal(BaseButton.SignalName.Pressed);
                SendBattleMouse(enemy.Position); _itemActionChecks++; _itemActionStage = 13;
            }
        }
        catch (Exception error) { _ = FinishSmokeAsync(false, error.Message); }
    }

    private void PrepareItemActionsFixture(GameSession session)
    {
        _itemActionSession = session;
        var party = session.Party!;
        // Only this explicitly named smoke fixture changes balance. Original
        // definitions and imported files remain untouched, including the enemy.
        foreach (var (word, value) in new[] { (RoleWords.MaximumHp, 999), (RoleWords.Hp, 100), (RoleWords.MaximumMp, 999),
            (RoleWords.Mp, 999), (RoleWords.PhysicalPower, 100), (RoleWords.MpType, 2), (RoleWords.Sex, 2), (RoleWords.Iq, 100),
            (RoleWords.Defence, 1000), (RoleWords.Speed, 100), (RoleWords.UsePoison, 120), (RoleWords.Detoxification, 120),
            (RoleWords.Medicine, 120), (RoleWords.HiddenWeapon, 120), (RoleWords.Hurt, 20), (RoleWords.Poison, 30) }) party.SetWord(0, word, value);
        var items = session.BattleResources!.Items.Values;
        var role = party.Roles[0];
        _actionMedicine = items.First(item => item[ItemWords.ItemType] == 3 && item[ItemWords.ShowIntroduction] == 1 && item[ItemWords.AddHp] is > 0 and <= 30).Id;
        _actionEquipment = items.First(item => item[ItemWords.ItemType] == 1 && item[ItemWords.ShowIntroduction] == 1 && item[ItemWords.AddSpeed] >= 0 &&
            ItemEligibilityRules.Check(role, item, ItemUseMode.Equip).Eligible).Id;
        _actionBook = items.First(item => item[ItemWords.ItemType] == 2 && item[ItemWords.ShowIntroduction] == 1 && item.Id is not (78 or 93) &&
            party.ItemUsers.GetValueOrDefault(item.Id, -1) < 0 && ItemEligibilityRules.Check(role, item, ItemUseMode.Practice).Eligible).Id;
        _actionThrown = items.First(item => item[ItemWords.ItemType] == 4 && item[ItemWords.AddHp] == -30).Id;
        foreach (var id in new[] { _actionEquipment, _actionBook, _actionMedicine, _actionThrown }) ((IEventWorld)session).AddItem(id, 3);
        _actionMedicineCount = session.Inventory[_actionMedicine]; _actionEquipmentCount = session.Inventory[_actionEquipment];
        _actionBookCount = session.Inventory[_actionBook]; _actionThrownCount = session.Inventory[_actionThrown];
    }

    private void SelectInventorySmokeItem(AcceptDialog dialog, int id)
    {
        var entries = _world.Session!.Inventory.OrderBy(pair => pair.Key).ToArray();
        var index = Array.FindIndex(entries, item => item.Key == id);
        if (index < 0) throw new InvalidOperationException("Inventory fixture item missing.");
        var items = dialog.GetNode<ItemList>("InventoryContents/InventoryItems");
        items.Select(index); items.EmitSignal(ItemList.SignalName.ItemSelected, (long)index);
    }

    private void SelectBattleSmokeItem(int id)
    {
        var items = _battleControls.GetNode<OptionButton>("BattleItem");
        var index = items.GetItemIndex(id);
        if (index < 0) throw new InvalidOperationException("Battle fixture item missing.");
        items.Select(index); items.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }

    private void MoveItemSmokeToward(BattleSession battle, BattleCombatant actor, BattleCombatant enemy)
    {
        var target = battle.ReachableMoves().Where(pair => pair.Value > 0)
            .OrderBy(pair => Math.Abs(pair.Key.X - enemy.Position.X) + Math.Abs(pair.Key.Y - enemy.Position.Y))
            .ThenBy(pair => pair.Value).Select(pair => (GridPosition?)pair.Key).FirstOrDefault();
        if (target is { } cell && _itemActionMovedRound != battle.Round)
        {
            PressBattleButton("移动：点击可到达的格子"); SendBattleMouse(cell); _itemActionMovedRound = battle.Round;
        }
        else PressBattleButton("休息");
    }
}
