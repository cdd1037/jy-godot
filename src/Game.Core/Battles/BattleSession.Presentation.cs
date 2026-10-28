using JinYong.Core.Items;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed partial class BattleSession
{
    // Assigned by the main constructor; direct Core clients retain instant rules.
    private readonly bool _presentActions;
    private readonly Queue<BattlePresentationFrame> _presentationFrames = new();
    private readonly List<BattlePresentationSound> _presentationSounds = [];
    private BattlePresentationFrame? _presentationFrame;
    private long _presentationElapsedTicks;
    private bool _presentationCompletesAction;
    private int _presentationMagicSlot;
    private long _presentationToken;

    public bool IsPresenting => _presentationFrame is not null;
    public BattlePresentationFrame? Presentation => _presentationFrame?.Movement is { } movement
        ? _presentationFrame with { Movement = movement with { Progress = Math.Clamp((double)_presentationElapsedTicks / _presentationFrame.Duration.Ticks, 0, 1) } }
        : _presentationFrame;
    public IReadOnlyList<BattleCombatantSnapshot> CaptureCombatantSnapshots() => Array.AsReadOnly(_combatants.Select(actor => new BattleCombatantSnapshot(actor)).ToArray());

    public IReadOnlyList<BattlePresentationSound> TakePresentationSounds()
    {
        var result = Array.AsReadOnly(_presentationSounds.ToArray()); _presentationSounds.Clear(); return result;
    }
    public void AdvancePresentation(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var remaining = elapsed.Ticks;
        while (_presentationFrame is { } current)
        {
            if (current.WaitingForConfirmation) return;
            var needed = current.Duration.Ticks - _presentationElapsedTicks;
            if (remaining < needed) { _presentationElapsedTicks += remaining; return; }
            remaining -= needed; _presentationElapsedTicks = 0;
            if (!AdvancePresentationFrame()) return;
        }
    }
    public bool ConfirmPresentation(long token)
    {
        if (_presentationFrame is not { WaitingForConfirmation: true } current || current.Token != token) return false;
        _presentationElapsedTicks = 0; AdvancePresentationFrame(); return true;
    }
    private bool AdvancePresentationFrame()
    {
        if (_presentationFrames.TryDequeue(out var next)) { ActivatePresentationFrame(next); return true; }
        // Clear the lock before invoking the ordinary completion path. Its next
        // actor, round status and outcome run exactly once here.
        _presentationFrame = null;
        var finish = _presentationCompletesAction; _presentationCompletesAction = false;
        if (finish) EndAction();
        return false;
    }
    private void ActivatePresentationFrame(BattlePresentationFrame frame)
    {
        _presentationFrame = frame with { Token = ++_presentationToken };
        _presentationSounds.AddRange(frame.Sounds);
    }
    private bool DeferEndActionForPresentation()
    {
        if (!IsPresenting) return false;
        _presentationCompletesAction = true; return true;
    }
    // Action entry points call these after target/eligibility checks but before
    // numeric kernels, RNG or consumption. No snapshots/queues are changed here.
    internal void ValidateMagicPresentation(BattleCombatant actor, MagicDefinition magic)
    {
        if (_presentActions) BattlePresentationPlans.ValidateMagicMetadata(actor.Role,
            magic[MagicWords.MagicType], magic[MagicWords.EffectId]);
    }
    internal void ValidateSupportPresentation(BattleCombatant actor, BattleSupportAction action)
    {
        if (!_presentActions) return;
        var effect = action switch { BattleSupportAction.UsePoison => 30, BattleSupportAction.Detox => 36,
            BattleSupportAction.Medicine => 0, _ => throw new ArgumentOutOfRangeException(nameof(action)) };
        BattlePresentationPlans.ValidateMagicMetadata(actor.Role, 0, effect);
    }
    internal void ValidateThrownItemPresentation(ItemDefinition item)
    {
        if (_presentActions) BattlePresentationPlans.ValidateEffectMetadata(item[ItemWords.HiddenWeaponEffectId]);
    }
    private void QueuePresentation(BattlePresentationPlan plan)
    {
        if (!_presentActions) return;
        var frames = plan.Frames;
        if (frames.Count == 0)
        {
            // A signed nonpositive magic frame count still preloads its banks.
            _presentationSounds.AddRange(plan.InitialSounds); return;
        }
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            if (frame.Duration < TimeSpan.Zero || frame.Duration == TimeSpan.Zero && !frame.WaitingForConfirmation)
                throw new ArgumentException("Timed presentation frames must have a positive duration.");
            if (index == 0 && plan.InitialSounds.Count > 0)
                frame = frame with { Sounds = Array.AsReadOnly(plan.InitialSounds.Concat(frame.Sounds).ToArray()) };
            _presentationFrames.Enqueue(frame);
        }
        if (_presentationFrame is null && _presentationFrames.TryDequeue(out var first))
        {
            ActivatePresentationFrame(first); _presentationElapsedTicks = 0;
        }
    }
    internal void QueueMovementPresentation(IReadOnlyList<BattleCombatantSnapshot> before, int actorSlot, IReadOnlyList<GridPosition> path)
    {
        if (_presentActions) QueuePresentation(BattlePresentationPlans.Movement(before, CaptureCombatantSnapshots(), actorSlot, path));
    }
    internal void QueueMagicPresentation(IReadOnlyList<BattleCombatantSnapshot> before, int actorSlot, int magicSlot,
        GridPosition target, Direction direction, IReadOnlyList<BattleDamageEvent>? damage = null)
    {
        _presentationMagicSlot = magicSlot;
        if (!_presentActions) return;
        var actor = _combatants.Single(person => person.Slot == actorSlot);
        var old = before.Single(person => person.Slot == actorSlot);
        var role = new RoleState(actor.Role.Definition);
        for (var word = 0; word < RoleWords.Count; word++) role.Set(word, old.Words[word]);
        var magic = _magics[role[RoleWords.MagicIdBegin + magicSlot]];
        var profile = BattleMagicRules.Profile(role, magic, magicSlot);
        var cells = BattleMagicRules.AreaCells(profile, old.Position, target, direction).Select(cell => cell.Position).Distinct().ToArray();
        var values = (damage ?? LastDamage).Select(hit => new BattlePresentationDamage(hit.TargetSlot, hit.Position, hit.Damage,
            hit.MpDamage ? BattleDamageKind.Mp : BattleDamageKind.Hp)).ToArray();
        // One hook is one attack iteration. The caller queues each native repeat
        // separately while preserving that iteration's before/after and damage.
        QueuePresentation(BattlePresentationPlans.Magic(before, CaptureCombatantSnapshots(), actorSlot,
            magic[MagicWords.MagicType], magic[MagicWords.EffectId], magic[MagicWords.SoundId], cells, values));
    }
    internal void QueueSupportPresentation(IReadOnlyList<BattleCombatantSnapshot> before, int actorSlot, BattleSupportResult result)
    {
        if (!_presentActions) return;
        var actor = before.Single(person => person.Slot == actorSlot); var effect = result.Action switch
        {
            BattleSupportAction.UsePoison => 30, BattleSupportAction.Detox => 36, BattleSupportAction.Medicine => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
        // DOS support reads the most recently used magic slot for attack audio;
        // unlike current OpenLegend's bug fix, do not force that track silent.
        var magicId = actor.Words[RoleWords.MagicIdBegin + _presentationMagicSlot];
        var sound = _magics.TryGetValue(magicId, out var magic) ? magic[MagicWords.SoundId] : -1;
        var kind = result.Action switch { BattleSupportAction.UsePoison => BattleDamageKind.Poison,
            BattleSupportAction.Detox => BattleDamageKind.Detox, _ => BattleDamageKind.Medicine };
        BattlePresentationDamage[] damage = result.TargetSlot is { } target ? [new(target, result.Position, result.Amount, kind)] : [];
        QueuePresentation(BattlePresentationPlans.Magic(before, CaptureCombatantSnapshots(), actorSlot, 0, effect, sound,
            [result.Position], damage, suppressFlash: result.Action != BattleSupportAction.UsePoison, flashKind: kind));
    }
    internal void QueueThrownItemPresentation(IReadOnlyList<BattleCombatantSnapshot> before, int actorSlot, int itemId, GridPosition target)
    {
        if (!_presentActions) return;
        var damage = LastDamage.Where(hit => hit.Damage != 0)
            .Select(hit => new BattlePresentationDamage(hit.TargetSlot, hit.Position, hit.Damage, BattleDamageKind.Hp)).ToArray();
        QueuePresentation(BattlePresentationPlans.ThrownItem(before, CaptureCombatantSnapshots(), actorSlot,
            _items[itemId][ItemWords.HiddenWeaponEffectId], [target], damage));
    }
    internal void QueueItemPresentation(IReadOnlyList<BattleCombatantSnapshot> before, int actorSlot, ItemUseResult result)
    {
        if (_presentActions) QueuePresentation(BattlePresentationPlans.Item(before, CaptureCombatantSnapshots(), actorSlot, result,
            waitForNoEffect: !before.Single(person => person.Slot == actorSlot).IsFriendly));
    }
    internal void QueueRestPresentation(IReadOnlyList<BattleCombatantSnapshot> before, int actorSlot)
    {
        // Native rest has no FIGHT/EFT animation. Do not invent one.
    }
}
