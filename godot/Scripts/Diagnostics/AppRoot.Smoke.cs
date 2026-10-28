using Godot;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using System.Text.Json;

namespace JinYong.Host;

public partial class AppRoot
{
    private string? _dialogueCapturePath;
    private bool _dialogueCaptured, _capturingDialogue, _smokeConfirmPressed;
    private readonly HashSet<long> _smokePages = [];
    private string? _capturePath;
    private string? _reportPath;
    private bool _smoke;
    private bool _inventoryCaptured, _capturingInventory;
    private long _smokeStarted;
    private int _smokeFrames;
    private List<Direction>? _smokeRoute;
    private int _smokeIndex;
    private string? _smokeAction;
    private bool _smokeAcknowledged;
    private bool _smokeQuickTapPending;
    private int _smokeQuickTapChecks;
    private int _smokeMoves;
    private int _smokeBlocks;
    private string _lastBlock = "";

    private void AdvanceSmoke()
    {
        if (System.Environment.TickCount64 - _smokeStarted > 45000)
        {
            _ = FinishSmokeAsync(false, "Smoke test exceeded 45 seconds.");
            return;
        }
        if (_busy || _world.Session is null) return;
        _smokeFrames++;
        if (_smokeFrames > 3000) { _ = FinishSmokeAsync(false, "Smoke test exceeded 3000 frames."); return; }
        if (_smokeFrames < 20) return;
        if (_smokeConfirmPressed)
        {
            Input.ActionRelease("interact"); _smokeConfirmPressed = false;
            return;
        }
        if (_world.Session.Events?.Error is { } eventError) { _ = FinishSmokeAsync(false, eventError); return; }
        if (_saveSmoke) { AdvanceSaveSmoke(); return; }
        if (_itemActionsSmoke) { AdvanceItemActionsSmoke(); return; }
        if (_travelSmoke) { AdvanceTravelSmoke(); return; }
        if (_battleSmoke) { AdvanceBattleSmoke(); return; }
        if (_eventSmoke) { AdvanceEventSmoke(); return; }
        if (_world.Session.IsEventRunning)
        {
            if (_world.Session.Events?.Pending is { } pending)
            {
                if (!_dialogueCaptured && _dialogueCapturePath is not null && DisplayServer.GetName() != "headless")
                {
                    if (!_capturingDialogue) _ = CaptureDialogueAsync();
                    return;
                }
                _smokePages.Add(pending.Token);
                Input.ActionPress("interact"); _smokeConfirmPressed = true;
            }
            return;
        }
        if (_smokeRoute is null)
        {
            _smokeRoute = SmokeRoute.Create(_world.Session.Scene);
            if (_smokeRoute.Count < 2) { _ = FinishSmokeAsync(false, "No walking route and collision probe found."); return; }
        }
        if (_smokeAction is not null)
        {
            if (!_smokeAcknowledged) return;
            Input.ActionRelease(_smokeAction);
            _smokeAction = null;
            _smokeIndex++;
            return; // Present a released input frame, including consecutive equal directions.
        }
        if (_world.IsMoving) return;
        if (_smokeIndex >= _smokeRoute.Count)
        {
            var openingPassed = CurrentPackage?.Story?.InitialProgramId is not { } initial ||
                (_world.Session.Events!.CompletedPrograms.Count(id => id == initial) == 1 &&
                 _world.Session.Scene.Events[CurrentPackage.Story.InitialEventIndex][4] == -1 &&
                 (_world.PlayOpening ? _smokePages.Count > 0 : _smokePages.Count == 0));
            _ = FinishSmokeAsync(openingPassed && _smokeMoves == _smokeRoute.Count - 1 && _smokeBlocks == 1 && _world.DrawnSprites > 0,
                _world.PlayOpening ? "Opening completed through input confirmations; then walked and tested collision."
                    : "Started in free roam with opening state applied; then walked and tested collision.");
            return;
        }
        _smokeAcknowledged = false;
        _smokeAction = ActionName(_smokeRoute[_smokeIndex]);
        Input.ActionPress(_smokeAction);
        if (_smokeIndex == 0)
        {
            Input.ActionRelease(_smokeAction);
            _smokeQuickTapPending = true;
        }
    }

    private async Task CaptureDialogueAsync()
    {
        _capturingDialogue = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.GetFullPath(_dialogueCapturePath!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { await FinishSmokeAsync(false, "Dialogue screenshot failed."); return; }
        _dialogueCaptured = true;
        _capturingDialogue = false;
    }

    private async Task CaptureInventoryAsync()
    {
        _capturingInventory = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_capturePath!))!, Path.GetFileNameWithoutExtension(_capturePath) + "-inventory.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { await FinishSmokeAsync(false, "Inventory screenshot failed."); return; }
        _inventoryCaptured = true;
        _capturingInventory = false;
    }

    private async Task FinishSmokeAsync(bool success, string message)
    {
        if (_finishing) return;
        _finishing = true;
        if (_smokeAction is not null) Input.ActionRelease(_smokeAction);
        if (_capturePath is not null && DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_capturePath))!);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(_capturePath) != Error.Ok) { success = false; message += " Screenshot failed."; }
        }
        var report = new
        {
            success, message, scene = CurrentPackage?.Scene.Id, position = _world.Session?.Player.Position,
            steps = _smokeMoves, blockedMoves = _smokeBlocks, lastBlock = _lastBlock,
            drawnSprites = _world.DrawnSprites, frames = _smokeFrames, package = CurrentPackage?.Directory
            , dialoguePages = _smokePages.Count, completedPrograms = _world.Session?.Events?.CompletedPrograms,
            playedSounds = _playedSounds, openingTrigger = _world.Session?.Scene.Events[0][4]
            , quickTapChecks = _smokeQuickTapChecks, smoothMovement = _world.SmoothMovement, playOpening = _world.PlayOpening
            , eventChoiceChecks = _eventSmokeChoiceChecks, inventoryUseChecks = _eventSmokeInventoryChecks,
            travelChecks = _travelChecks,
            saveChecks = _saveChecks,
            itemActionChecks = _itemActionChecks,
            battleChecks = _battleChecks, battleMoves = _battleMoves, battleAttacks = _battleAttacks,
            battlePresentationFrames = _battlePresentationFrames,
            battlePresentationStages = _battlePresentationStages.Select(stage => stage.ToString()),
            battlePresentationSprites = _battlePresentationSprites,
            battleItemConfirmations = _battleItemConfirmations, playedBattleSounds = _battleController.PlayedSounds,
            battleResultConfirmations = _battleResultConfirmations,
            battleOutcome = _world.Session?.LastBattleOutcome,
            variables = _world.Session?.Variables, inventory = _world.Session?.Inventory,
            eventTrace = _world.Session?.Events?.Trace
        };
        if (_reportPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_reportPath))!);
            File.WriteAllText(_reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        GD.Print((success ? "SMOKE_PASS " : "SMOKE_FAIL ") + JsonSerializer.Serialize(report));
        GetTree().Quit(success ? 0 : 1);
    }

    private void CompleteSmokeQuickTap()
    {
        if (_smokeQuickTapPending)
        {
            _smokeQuickTapPending = false;
            if (!_smokeAcknowledged) _ = FinishSmokeAsync(false, "A direction pressed and released in one frame was lost.");
            else _smokeQuickTapChecks++;
        }
    }

    private void RecordSmokeMovement(MovementResult result)
    {
        _lastBlock = result.Block.ToString();
        if (_smokeAction is not null)
        {
            if (result.Moved) _smokeMoves++; else _smokeBlocks++;
            _smokeAcknowledged = true;
        }
    }
}
