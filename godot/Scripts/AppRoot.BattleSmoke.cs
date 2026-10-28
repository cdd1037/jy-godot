using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class AppRoot
{
    private bool _battleSmoke, _battleCaptured, _capturingBattle, _battleMouseDown;
    private int _battleSmokeStage, _battleChecks, _battleMoves, _battleAttacks, _battleMovedRound = -1;
    private BattleCombatant? _battleMovedActor;
    private GameSession? _battleOriginalSession;
    private SceneRuntimeState? _battleOriginalScene;
    private GridPosition _battleOriginalPosition;
    private Dictionary<int, int> _battleOriginalInventory = [];
    private BattleOutcome? _battleSmokeOutcome;
    private readonly HashSet<BattlePresentationStage> _battlePresentationStages = [];
    private readonly HashSet<string> _battlePresentationSprites = [];
    private BattleSession? _observedPresentationBattle;
    private long _observedPresentationToken;
    private int _battlePresentationFrames, _battleItemConfirmations;

    private void AdvanceBattleSmoke()
    {
        var session = _world.Session!;
        if (_battleMouseDown)
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false,
                Position = SmokeMousePosition(_world.GlobalPosition + _world.Size / 2) });
            _battleMouseDown = false;
            return;
        }
        if (_battleSmokeStage == 0)
        {
            _battleOriginalSession = session; _battleOriginalScene = session.Scene; _battleOriginalPosition = session.Player.Position;
            _battleOriginalInventory = session.Inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
            Input.ActionPress("interact"); _smokeConfirmPressed = true; _battleSmokeStage = 1;
            return;
        }
        if (session.Events?.Pending is { } pending)
        {
            if (pending.Kind == EventPresentationKind.Error) { _ = FinishSmokeAsync(false, pending.Text); return; }
            Input.ActionPress("interact"); _smokeConfirmPressed = true;
            return;
        }
        if (session.Battle is { } battle)
        {
            if (WaitForBattleSmokePresentation(battle)) return;
            if (battle.Phase == BattlePhase.PartySelection)
            {
                if (!PressBattleButton("确认出战")) return;
                _battleChecks++; _battleSmokeStage = 2;
                return;
            }
            if (battle.Phase == BattlePhase.Finished)
            {
                _battleSmokeOutcome = battle.Outcome;
                if (!PressBattleButton("返回场景，继续剧情")) return;
                _battleChecks++; _battleSmokeStage = 3;
                return;
            }
            if (battle.CurrentActor is not { IsFriendly: true } actor) return;
            if (_capturePath is not null && !_battleCaptured && DisplayServer.GetName() != "headless")
            {
                if (!_capturingBattle) _ = CaptureBattleAsync();
                return;
            }
            var target = battle.Combatants.Where(other => !other.IsFriendly && other.IsAlive)
                .OrderBy(other => Math.Abs(other.Position.X - actor.Position.X) + Math.Abs(other.Position.Y - actor.Position.Y)).First();
            var slot = battle.AvailableMagicSlots().FirstOrDefault(-1);
            var direction = BattleMagicRules.Facing(actor.Position, target.Position);
            if (slot >= 0 && battle.CanTargetMagic(slot, target.Position) && battle.AttackArea(slot, target.Position, direction).Any(cell => cell.Position == target.Position))
            {
                if (!PressBattleButton("攻击：点击目标格子")) return;
                SendBattleMouse(target.Position);
                return;
            }
            if (_battleMovedRound != battle.Round || !ReferenceEquals(_battleMovedActor, actor))
            {
                var destination = battle.ReachableMoves().Where(pair => pair.Value > 0)
                    .OrderBy(pair => Math.Abs(pair.Key.X - target.Position.X) + Math.Abs(pair.Key.Y - target.Position.Y))
                    .ThenBy(pair => pair.Value).Select(pair => (GridPosition?)pair.Key).FirstOrDefault();
                if (destination is { } cell)
                {
                    if (!PressBattleButton("移动：点击可到达的格子")) return;
                    SendBattleMouse(cell);
                    _battleMovedRound = battle.Round; _battleMovedActor = actor;
                    return;
                }
            }
            PressBattleButton("休息");
            return;
        }
        if (_battleSmokeStage < 3 || session.IsEventRunning) return;
        var expected = _battleSmokeOutcome == BattleOutcome.Victory ? 1 : 2;
        var same = ReferenceEquals(_battleOriginalSession, session) && ReferenceEquals(_battleOriginalScene, session.Scene) && session.Player.Position == _battleOriginalPosition;
        var bagSame = _battleOriginalInventory.OrderBy(pair => pair.Key).SequenceEqual(session.Inventory.OrderBy(pair => pair.Key));
        var resumed = session.Variables.GetValueOrDefault("example:battle/result") == expected && session.Events!.CompletedPrograms.Count(id => id == 11001) == 1;
        var settled = session.LastBattleSettlement is not null && session.LastBattleOutcome == _battleSmokeOutcome;
        if (same) _battleChecks++; if (bagSame) _battleChecks++; if (resumed) _battleChecks++; if (settled) _battleChecks++;
        var success = _battleChecks == 6 && _battleMoves > 0;
        _ = FinishSmokeAsync(success, success
            ? "Battle 0 completed through party selection and tactical input; settled its actual result and resumed the same scene event."
            : $"Battle flow failed: same={same}, bag={bagSame}, resumed={resumed}, settled={settled}, moves={_battleMoves}, attacks={_battleAttacks}.");
    }

    private bool WaitForBattleSmokePresentation(BattleSession battle)
    {
        if (battle.Presentation is not { } frame) return false;
        _battlePresentationStages.Add(frame.Stage);
        if (!ReferenceEquals(_observedPresentationBattle, battle) || _observedPresentationToken != frame.Token)
        {
            _observedPresentationBattle = battle; _observedPresentationToken = frame.Token;
            _battlePresentationFrames++;
        }
        if (frame.ActorSprite is { } actor) _battlePresentationSprites.Add($"{actor.Bank}:{actor.Frame}");
        foreach (var effect in frame.Effects) _battlePresentationSprites.Add($"{effect.Bank}:{effect.Frame}");
        if (frame.WaitingForConfirmation && PressBattleButton("继续")) _battleItemConfirmations++;
        return true;
    }

    private bool PressBattleButton(string text)
    {
        var button = _battleControls.GetChildren().OfType<Button>().FirstOrDefault(candidate => candidate.Text == text);
        if (button is null || button.Disabled) { _ = FinishSmokeAsync(false, $"Battle UI action unavailable: {text}"); return false; }
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private void SendBattleMouse(GridPosition cell)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
            Position = SmokeMousePosition(_world.GlobalPosition + _world.BattleCellScreenPosition(cell)) });
        _battleMouseDown = true;
    }

    // ParseInputEvent expects window/embedder coordinates. Popup creation can
    // change headless stretch; passing viewport coordinates would scale twice.
    private Vector2 SmokeMousePosition(Vector2 viewportPosition) => GetViewport().GetFinalTransform() * viewportPosition;

    private async Task CaptureBattleAsync()
    {
        _capturingBattle = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_capturePath!))!, Path.GetFileNameWithoutExtension(_capturePath) + "-battle.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { await FinishSmokeAsync(false, "Battle screenshot failed."); return; }
        _battleCaptured = true; _capturingBattle = false;
    }
}
