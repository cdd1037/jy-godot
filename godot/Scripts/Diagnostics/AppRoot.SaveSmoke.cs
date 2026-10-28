using Godot;
using JinYong.Content.Packages;
using JinYong.Content.Persistence;
using JinYong.Core.Events;
using System.Text.Json;

namespace JinYong.Host;

public partial class AppRoot
{
    private bool _saveSmoke;
    private int _saveSmokeStage, _saveChecks, _saveSilver;
    private SaveGameDocument? _savedCheckpoint;
    private bool _saveMenuCaptured, _capturingSaveMenu;
    private bool _saveDeliveryQueued;
    private ulong _saveDeliveryOpenedFrame;
    private JinYong.Core.Session.GameSession? _saveDeliverySession;

    private void PrepareSaveSmokeOverlay()
    {
        if (!_smoke || !_eventSmoke || _storyOverlay is null || _savesDirectory == ProjectSettings.GlobalizePath("user://saves"))
            throw new InvalidOperationException("Save smoke requires EventDemo and an isolated saves directory.");
        // Explicit synthetic host fixtures, not claims about a native story path.
        _storyOverlay = _storyOverlay with
        {
            Programs = [.._storyOverlay.Programs,
                new(10010, [new(0, EventOperation.Death, [83])]),
                new(10011, [new(0, EventOperation.LoadMenu, [])])]
        };
    }

    private void AdvanceSaveSmoke()
    {
        var session = _world.Session!;
        if (_saveDeliveryQueued)
        {
            if (_saveDeliveryOpenedFrame == Engine.GetProcessFrames()) return;
            try
            {
                if (!ReferenceEquals(session, _saveDeliverySession) || !_inventoryController.IsOpen)
                    throw new InvalidOperationException("Queued inventory delivery lost its session or panel.");
                var panel = _inventoryController.Panel;
                var index = Array.FindIndex(session.Inventory.OrderBy(pair => pair.Key).ToArray(), pair => pair.Key == 9000);
                if (index < 0) throw new InvalidOperationException("Saved quest token is missing.");
                panel.Items.Select(index);
                panel.Items.EmitSignal(ItemList.SignalName.ItemSelected, (long)index);
                var use = panel.UseButton; // Selecting creates a new rendered generation.
                if (use.Disabled) throw new InvalidOperationException("Saved quest token cannot be used.");
                use.EmitSignal(BaseButton.SignalName.Pressed);
                _saveDeliveryQueued = false; _saveDeliverySession = null;
            }
            catch (Exception error) { _saveDeliveryQueued = false; _ = FinishSmokeAsync(false, error.Message); }
            return;
        }
        if (session.PendingLifecycle is { } lifecycle)
        {
            if (_saveSmokeStage == 7 && _capturePath is not null && !_saveMenuCaptured && DisplayServer.GetName() != "headless")
            {
                if (!_capturingSaveMenu) _ = CaptureSaveMenuAsync();
                return;
            }
            try
            {
                if (_saveSmokeStage is not (7 or 9) || !SaveMenuVisible || _shownLifecycle != lifecycle || _saveCancel.Visible)
                    throw new InvalidOperationException("The lifecycle menu was absent or cancelable.");
                var expected = _saveSmokeStage == 7 ? EventLifecycleKind.Death : EventLifecycleKind.LoadMenu;
                if (lifecycle.Kind != expected) throw new InvalidOperationException("Lifecycle menu kind mismatch.");
                CloseSaveMenu();
                _saveQuit.EmitSignal(BaseButton.SignalName.Pressed);
                _saveConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled); _saveConfirmation.Hide();
                if (!SaveMenuVisible || TryLoadSlot(2) || !ReferenceEquals(session, _world.Session) || session.PendingLifecycle != lifecycle)
                    throw new InvalidOperationException("Cancel/failed load escaped the lifecycle or changed its token.");
                _saveSlots[0].EmitSignal(BaseButton.SignalName.Pressed);
                if (SaveMenuVisible || !session.IsRetired || _world.Session!.IsEventRunning)
                    throw new InvalidOperationException("Lifecycle load did not replace and retire the old script.");
                _saveChecks++; _saveSmokeStage++;
            }
            catch (Exception error) { _ = FinishSmokeAsync(false, error.Message); }
            return;
        }
        if (session.IsEventRunning)
        {
            if (session.Events?.Pending is { } pending)
            {
                _smokePages.Add(pending.Token);
                Input.ActionPress("interact"); _smokeConfirmPressed = true;
            }
            return;
        }
        if (session.IsInputLocked || _world.IsMoving) return;
        try
        {
            switch (_saveSmokeStage)
            {
                case 0:
                    if (!_eventSmoke || CurrentPackage?.Library is null || Directory.Exists(_savesDirectory))
                        throw new InvalidOperationException("Save smoke requires EventDemo and a fresh dedicated saves directory.");
                    _saveSilver = session.Inventory.GetValueOrDefault(174);
                    Input.ActionPress("interact"); _smokeConfirmPressed = true;
                    _saveSmokeStage = 1;
                    break;
                case 1:
                    if (session.Variables.GetValueOrDefault("example:quest/progress") != 1 || session.Inventory.GetValueOrDefault(9000) != 1)
                        throw new InvalidOperationException("Quest token was not granted before saving.");
                    _saveButton.EmitSignal(BaseButton.SignalName.Pressed);
                    if (!SaveMenuVisible || !_saving) throw new InvalidOperationException("Save button did not open the slot menu.");
                    _saveSlots[0].EmitSignal(BaseButton.SignalName.Pressed);
                    if (SaveMenuVisible) throw new InvalidOperationException(_saveError.Text);
                    _savedCheckpoint = SaveStore().Read(1); _saveChecks++;
                    _saveSmokeStage = 2;
                    break;
                case 2:
                    // Exercise both cancel and confirm on an existing slot, through the UI.
                    _saveButton.EmitSignal(BaseButton.SignalName.Pressed);
                    _saveSlots[0].EmitSignal(BaseButton.SignalName.Pressed);
                    if (!_saveConfirmation.Visible) throw new InvalidOperationException("Overwrite was not confirmed explicitly.");
                    _saveConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled); _saveConfirmation.Hide();
                    if (!SaveMenuVisible) throw new InvalidOperationException("Canceling overwrite dismissed the game menu.");
                    _saveSlots[0].EmitSignal(BaseButton.SignalName.Pressed);
                    _saveConfirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed); _saveConfirmation.Hide();
                    if (SaveMenuVisible) throw new InvalidOperationException(_saveError.Text);
                    _savedCheckpoint = SaveStore().Read(1); _saveChecks++;
                    DeliverSaveSmokeToken(); _saveSmokeStage = 3;
                    break;
                case 3:
                    CheckSaveSmokeDelivery(); _saveChecks++;
                    _loadButton.EmitSignal(BaseButton.SignalName.Pressed);
                    if (!SaveMenuVisible || _saving) throw new InvalidOperationException("Load button did not open its slot menu.");
                    // A corrupt slot is visible as invalid and must leave the live game intact.
                    File.WriteAllText(SaveStore().GetSlotPath(2), "{");
                    RefreshSaveSlots();
                    if (!_saveSlots[1].Disabled || TryLoadSlot(2) || !ReferenceEquals(session, _world.Session) || session.IsRetired || !SaveMenuVisible)
                        throw new InvalidOperationException("A corrupt load replaced or dismissed the live game.");
                    _saveChecks++;
                    _storyOverlay = null; // Restart-like case: saved overlay is the only source of its definitions.
                    _saveSlots[0].EmitSignal(BaseButton.SignalName.Pressed);
                    if (SaveMenuVisible || ReferenceEquals(session, _world.Session) || !session.IsRetired || _storyOverlay is null)
                        throw new InvalidOperationException("The valid slot did not replace the old session and restore its overlay.");
                    var restored = _world.Session!;
                    if (JsonSerializer.Serialize(_savedCheckpoint!.Snapshot, ContentJson.Options) !=
                        JsonSerializer.Serialize(restored.CaptureSnapshot(), ContentJson.Options))
                        throw new InvalidOperationException("Loaded session differs from its full saved checkpoint.");
                    _saveChecks += 2;
                    _saveSmokeStage = 4;
                    break;
                case 4:
                    if (session.Variables.GetValueOrDefault("example:quest/progress") != 1 || session.Inventory.GetValueOrDefault(9000) != 1 ||
                        session.Inventory.GetValueOrDefault(174) != _saveSilver)
                        throw new InvalidOperationException("Quest state, token or reward was not restored.");
                    DeliverSaveSmokeToken(); _saveSmokeStage = 5;
                    break;
                case 5:
                    CheckSaveSmokeDelivery(); _saveChecks++;
                    Input.ActionPress("interact"); _smokeConfirmPressed = true; _saveSmokeStage = 6;
                    break;
                case 6:
                    CheckSaveSmokeDelivery(); _saveChecks++;
                    if (!session.Events!.TryStart(10010, 1, session.Player.Position))
                        throw new InvalidOperationException("Synthetic death fixture did not start.");
                    _saveSmokeStage = 7;
                    break;
                case 8:
                    if (!session.Events!.TryStart(10011, 1, session.Player.Position))
                        throw new InvalidOperationException("Synthetic load-menu fixture did not start.");
                    _saveSmokeStage = 9;
                    break;
                default:
                    _ = FinishSmokeAsync(_saveChecks == 10 && _world.DrawnSprites > 0,
                        "Saved/overwrote through UI, completed quest, rejected corrupt load, restored full checkpoint and embedded overlay without replaying rewards; synthetic death/load-menu fixtures stayed terminal through cancel/failure and replaced the old VM on success.");
                    break;
            }
        }
        catch (Exception error) { _ = FinishSmokeAsync(false, error.Message); }
    }

    private void CheckSaveSmokeDelivery()
    {
        var session = _world.Session!;
        if (session.Variables.GetValueOrDefault("example:quest/progress") != 2 || session.Inventory.GetValueOrDefault(9000) != 0 ||
            session.Inventory.GetValueOrDefault(174) != _saveSilver + 100)
            throw new InvalidOperationException("Quest delivery/reward was incorrect after saving or loading.");
    }

    private async Task CaptureSaveMenuAsync()
    {
        _capturingSaveMenu = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_capturePath!))!,
            Path.GetFileNameWithoutExtension(_capturePath) + "-death-menu.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { await FinishSmokeAsync(false, "Save-menu screenshot failed."); return; }
        _saveMenuCaptured = true; _capturingSaveMenu = false;
    }

    private void DeliverSaveSmokeToken()
    {
        _bagButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (!_inventoryController.IsOpen) throw new InvalidOperationException("Inventory did not open.");
        // Keep the production opening-frame input barrier: dispatch after a real
        // host process boundary, without adding a bypass to the controller.
        _saveDeliveryQueued = true;
        _saveDeliveryOpenedFrame = Engine.GetProcessFrames();
        _saveDeliverySession = _world.Session;
    }
}
