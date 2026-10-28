using Godot;
using JinYong.Core.Events;

namespace JinYong.Host;

public partial class AppRoot
{
    private bool _eventSmoke;
    private int _eventSmokeStage, _eventSmokeChoiceChecks, _eventSmokeInventoryChecks;
    private int _eventSmokeSilver;

    private void AdvanceEventSmoke()
    {
        var session = _world.Session!;
        var events = session.Events!;
        if (_eventSmokeStage == 0)
        {
            if (session.IsEventRunning)
            {
                if (events.Pending is not null) { Input.ActionPress("interact"); _smokeConfirmPressed = true; }
                return;
            }
            _eventSmokeSilver = session.Inventory.GetValueOrDefault(174);
            Input.ActionPress("interact"); _smokeConfirmPressed = true;
            _eventSmokeStage = 1;
            return;
        }
        if (events.Pending is { } pending)
        {
            if (pending.Kind == EventPresentationKind.Choice)
            {
                if (!_dialogueCaptured && _dialogueCapturePath is not null && DisplayServer.GetName() != "headless")
                {
                    if (!_capturingDialogue) _ = CaptureDialogueAsync();
                    return;
                }
                if (_eventSmokeChoiceChecks++ != 0) { _ = FinishSmokeAsync(false, "Quest returned to its initial choice."); return; }
            }
            Input.ActionPress("interact"); _smokeConfirmPressed = true;
            return;
        }
        if (session.IsEventRunning) return;
        if (_eventSmokeStage == 1)
        {
            if (session.Variables.GetValueOrDefault("example:quest/progress") != 1 || session.Inventory.GetValueOrDefault(9000) != 1)
            { _ = FinishSmokeAsync(false, "Quest acceptance did not grant its token."); return; }
            _bagButton.EmitSignal(BaseButton.SignalName.Pressed);
            _eventSmokeStage = 2;
            return;
        }
        if (_eventSmokeStage == 2)
        {
            if (_inventoryDialog is not { Visible: true } dialog) { _ = FinishSmokeAsync(false, "Inventory did not open."); return; }
            if (_capturePath is not null && !_inventoryCaptured && DisplayServer.GetName() != "headless")
            {
                if (!_capturingInventory) _ = CaptureInventoryAsync();
                return;
            }
            var items = dialog.GetNode<ItemList>("InventoryContents/InventoryItems");
            var use = dialog.GetNode<Button>("InventoryContents/UseItem");
            var index = Array.FindIndex(session.Inventory.OrderBy(pair => pair.Key).ToArray(), pair => pair.Key == 9000);
            items.Select(index);
            items.EmitSignal(ItemList.SignalName.ItemSelected, (long)index);
            if (use.Disabled) { _ = FinishSmokeAsync(false, "Inventory item could not be selected for use."); return; }
            use.EmitSignal(BaseButton.SignalName.Pressed);
            _eventSmokeInventoryChecks++;
            _eventSmokeStage = 3;
            return;
        }
        if (_eventSmokeStage == 3)
        {
            if (session.Variables.GetValueOrDefault("example:quest/progress") != 2 || session.Inventory.GetValueOrDefault(9000) != 0 ||
                session.Inventory.GetValueOrDefault(174) != _eventSmokeSilver + 100)
            { _ = FinishSmokeAsync(false, "Quest delivery state or reward is incorrect."); return; }
            Input.ActionPress("interact"); _smokeConfirmPressed = true;
            _eventSmokeStage = 4;
            return;
        }
        _ = FinishSmokeAsync(_eventSmokeChoiceChecks == 1 && _eventSmokeInventoryChecks == 1 &&
            session.Inventory.GetValueOrDefault(174) == _eventSmokeSilver + 100 && session.Inventory.GetValueOrDefault(9000) == 0,
            "Accepted a quest through choice input, used its token through inventory, received one reward, and tested repeat interaction.");
    }
}
