using JinYong.Core.Items;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

/// <summary>
/// Native frame contracts: OpenLegend historical Z_DAT/0x37355, 0x3859E,
/// 0x3884A and 0x38910. The 53 frame counts are the original word_55ACE table,
/// not pictures copied from a resource archive. DOS delay(n) waits n/40+1 BIOS
/// tick changes; our one host clock approximates those ticks with elapsed time.
/// </summary>
public static class BattlePresentationPlans
{
    public static TimeSpan BiosTick { get; } = TimeSpan.FromTicks(549254);
    private static readonly IReadOnlyList<int> EffectCounts = Array.AsReadOnly(new[]
    {
        10,14,17,9,13,17,17,17,18,19,19,15,13,10,10,15,21,16,
        9,11,8,9,8,8,7,8,8,9,12,19,11,14,12,17,8,11,
        9,13,10,19,14,17,19,14,21,16,13,18,14,17,17,16,7
    });
    public static IReadOnlyList<int> EffectFrameCounts => EffectCounts;
    public static TimeSpan Delay(int originalParameter) => originalParameter >= 0
        ? TimeSpan.FromTicks(checked(BiosTick.Ticks * (originalParameter / 40 + 1L)))
        : throw new ArgumentOutOfRangeException(nameof(originalParameter));
    public static int EffectStartFrame(int effectId)
    {
        CheckEffect(effectId); return 13000 + 2 * EffectCounts.Take(effectId).Sum();
    }
    private static void CheckEffect(int id)
    { if (id < 0 || id >= EffectCounts.Count) throw new ArgumentOutOfRangeException(nameof(id), "Original effects use IDs 0..52."); }
    public static void ValidateEffectMetadata(int effectId) => CheckEffect(effectId);
    public static void ValidateMagicMetadata(RoleState role, int magicType, int effectId)
    {
        ArgumentNullException.ThrowIfNull(role); CheckEffect(effectId);
        if (magicType is < 0 or > 4 || role[RoleWords.HeadId] is < 0 or > short.MaxValue ||
            Enumerable.Range(25, 15).Any(word => role[word] is < short.MinValue or > short.MaxValue))
            throw new ArgumentException("Original FIGHT animation requires a valid head, type and signed-word frame metadata.");
        var actorFrames = role[25 + magicType];
        var count = BattleAttackRules.Word(role[30 + magicType] + EffectCounts[effectId] - 1);
        if (actorFrames <= 0 || count <= 0) return; // Original signed zero-frame boundary remains legal.
        long prior = Enumerable.Range(25, magicType).Sum(word => (long)role[word]);
        var first = 16000 + 8 * prior;
        var last = first + 6L * actorFrames + 2L * (Math.Min(actorFrames, count) - 1);
        // DOS stores the generated sprite in a signed word; negative/wrapped
        // atlas pointers are outside the supported resource-address domain.
        if (first < 0 || last > short.MaxValue)
            throw new ArgumentException("FIGHT metadata would generate invalid signed sprite IDs.");
    }
    private static IReadOnlyList<BattleCombatantSnapshot> Freeze(IReadOnlyList<BattleCombatantSnapshot> source) => Array.AsReadOnly(source.ToArray());
    private static IReadOnlyList<BattleCombatantSnapshot> VisibleAfter(IReadOnlyList<BattleCombatantSnapshot> before,
        IReadOnlyList<BattleCombatantSnapshot> after) => Array.AsReadOnly(after.Select(person => person with
        { Hidden = before.Single(old => old.Slot == person.Slot).Hidden }).ToArray());
    private static IReadOnlyList<BattleCombatantSnapshot> VisibleBefore(IReadOnlyList<BattleCombatantSnapshot> before,
        IReadOnlyList<BattleCombatantSnapshot> after, int actorSlot) => Array.AsReadOnly(before.Select(person => person.Slot == actorSlot
            ? person with { Facing = after.Single(current => current.Slot == actorSlot).Facing } : person).ToArray());

    public static BattlePresentationPlan Movement(IReadOnlyList<BattleCombatantSnapshot> before,
        IReadOnlyList<BattleCombatantSnapshot> after, int actorSlot, IReadOnlyList<GridPosition> path)
    {
        before = Freeze(before); after = Freeze(after);
        if (path.Count < 2 || path.Any(position => !BattlefieldDefinition.Contains(position)))
            throw new ArgumentException("Movement presentation requires a valid path containing source and destination.");
        var actor = before.Single(person => person.Slot == actorSlot);
        if (actor.Position != path[0] || after.Single(person => person.Slot == actorSlot).Position != path[^1] ||
            path.Zip(path.Skip(1)).Any(pair => Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Y - pair.Second.Y) != 1))
            throw new ArgumentException("Movement presentation path does not match its snapshots.");
        var frames = new List<BattlePresentationFrame>();
        for (var step = 1; step < path.Count; step++)
        {
            var from = path[step - 1]; var to = path[step]; var facing = BattleMagicRules.Facing(from, to);
            var moving = actor with { Facing = facing };
            var visible = Array.AsReadOnly(before.Select(person => person.Slot == actorSlot ? person with { Position = from, Facing = facing } : person).ToArray());
            frames.Add(new()
            {
                Stage = BattlePresentationStage.Movement, ActorSlot = actorSlot, FrameIndex = step - 1, FrameCount = path.Count - 1,
                Duration = Delay(40), Before = before, After = after, Combatants = visible,
                ActorSprite = new("battlefield", moving.SpriteId, from),
                Movement = new(from, to, 0)
            });
        }
        return new(frames);
    }

    public static BattlePresentationPlan Magic(IReadOnlyList<BattleCombatantSnapshot> before,
        IReadOnlyList<BattleCombatantSnapshot> after, int actorSlot, int magicType, int effectId, int soundId,
        IReadOnlyCollection<GridPosition> effectPositions, IReadOnlyList<BattlePresentationDamage> damage, bool suppressFlash = false,
        BattleDamageKind? flashKind = null)
    {
        before = Freeze(before); after = Freeze(after); CheckEffect(effectId);
        if (magicType is < 0 or > 4 || effectPositions.Any(position => !BattlefieldDefinition.Contains(position)))
            throw new ArgumentException("Invalid original animation type or effect position.");
        var actor = before.Single(person => person.Slot == actorSlot);
        var currentActor = after.Single(person => person.Slot == actorSlot);
        var words = actor.Words; var actorFrames = words[25 + magicType];
        var effectStart = BattleAttackRules.Word(words[30 + magicType] - 1);
        var attackStart = BattleAttackRules.Word(words[35 + magicType] - 1);
        var count = BattleAttackRules.Word(words[30 + magicType] + EffectCounts[effectId] - 1);
        var firstFight = 16000 + 8 * words.Skip(25).Take(magicType).Sum() + 2 * actorFrames * (int)currentActor.Facing;
        var effectFrame = EffectStartFrame(effectId) - 2; bool attackStarted = false, effectStarted = false;
        BattlePresentationSprite? sprite = null;
        var frames = new List<BattlePresentationFrame>(); var visible = VisibleBefore(before, after, actorSlot);
        for (var frame = 0; frame < count; frame++)
        {
            if (frame < actorFrames) sprite = new($"fight/{actor.HeadId:D3}", BattleAttackRules.Word(firstFight + 2 * frame), actor.Position);
            var sounds = new List<BattlePresentationSound>();
            // Both flags may mature on this frame. Native dispatch starts attack
            // bank 1 before effect bank 2, even though effect's flag is set first.
            if (frame >= attackStart && !attackStarted)
            { attackStarted = true; if (soundId >= 0) sounds.Add(new(BattleSoundBank.Attack, soundId, BattleSoundAction.StartLoaded)); }
            var effects = Array.Empty<BattlePresentationSprite>();
            if (frame >= effectStart)
            {
                effectFrame += 2;
                if (!effectStarted) { effectStarted = true; sounds.Add(new(BattleSoundBank.Effect, effectId, BattleSoundAction.StartLoaded)); }
                effects = effectPositions.Select(position => new BattlePresentationSprite("effect", effectFrame, position)).ToArray();
            }
            frames.Add(new()
            {
                Stage = BattlePresentationStage.Fight, ActorSlot = actorSlot, FrameIndex = frame, FrameCount = count,
                Duration = Delay(17), Before = before, After = after, Combatants = visible, ActorSprite = sprite,
                Effects = Array.AsReadOnly(effects), Sounds = sounds.AsReadOnly()
            });
        }
        frames.AddRange(DamageFrames(before, after, actorSlot, damage, effectPositions, suppressFlash, sprite, flashKind));
        var loads = new List<BattlePresentationSound>();
        if (soundId >= 0) loads.Add(new(BattleSoundBank.Attack, soundId, BattleSoundAction.Load));
        loads.Add(new(BattleSoundBank.Effect, effectId, BattleSoundAction.Load));
        return new(frames, loads);
    }

    public static BattlePresentationPlan ThrownItem(IReadOnlyList<BattleCombatantSnapshot> before,
        IReadOnlyList<BattleCombatantSnapshot> after, int actorSlot, int effectId,
        IReadOnlyCollection<GridPosition> effectPositions, IReadOnlyList<BattlePresentationDamage> damage)
    {
        before = Freeze(before); after = Freeze(after); CheckEffect(effectId);
        var visible = VisibleBefore(before, after, actorSlot); var count = EffectCounts[effectId]; var first = EffectStartFrame(effectId);
        var frames = new List<BattlePresentationFrame>
        {
            new() { Stage = BattlePresentationStage.EffectPrelude, ActorSlot = actorSlot, FrameIndex = 0, FrameCount = 1,
                Duration = Delay(100), Before = before, After = after, Combatants = visible,
                Sounds = Array.AsReadOnly(new[] { new BattlePresentationSound(BattleSoundBank.Attack, 13, BattleSoundAction.StartLoaded) }) }
        };
        for (var frame = 0; frame < count; frame++)
            frames.Add(new()
            {
                Stage = BattlePresentationStage.Effect, ActorSlot = actorSlot, FrameIndex = frame, FrameCount = count,
                Duration = Delay(17), Before = before, After = after, Combatants = visible,
                Effects = Array.AsReadOnly(effectPositions.Select(position => new BattlePresentationSprite("effect", first + 2 * frame, position)).ToArray()),
                Sounds = frame == 0 ? Array.AsReadOnly(new[] { new BattlePresentationSound(BattleSoundBank.Effect, effectId, BattleSoundAction.StartLoaded) }) : Array.Empty<BattlePresentationSound>()
            });
        frames.AddRange(DamageFrames(before, after, actorSlot, damage, effectPositions, suppressFlash: false));
        return new(frames, [new(BattleSoundBank.Attack, 13, BattleSoundAction.Load), new(BattleSoundBank.Effect, effectId, BattleSoundAction.Load)]);
    }
    public static BattlePresentationPlan Item(IReadOnlyList<BattleCombatantSnapshot> before,
        IReadOnlyList<BattleCombatantSnapshot> after, int actorSlot, ItemUseResult result, bool waitForNoEffect = false)
    {
        before = Freeze(before); after = Freeze(after);
        var friendly = before.Single(person => person.Slot == actorSlot).IsFriendly;
        if (!result.HasEffect && !waitForNoEffect) return new([]);
        return new([new BattlePresentationFrame
        {
            Stage = BattlePresentationStage.Item, ActorSlot = actorSlot, FrameIndex = 0, FrameCount = 1,
            Duration = friendly ? TimeSpan.Zero : Delay(340), WaitingForConfirmation = friendly,
            Before = before, After = after, Combatants = VisibleAfter(before, after), ItemUse = result
        }]);
    }
    private static IEnumerable<BattlePresentationFrame> DamageFrames(IReadOnlyList<BattleCombatantSnapshot> before,
        IReadOnlyList<BattleCombatantSnapshot> after, int actorSlot, IReadOnlyList<BattlePresentationDamage> damage,
        IReadOnlyCollection<GridPosition> effectPositions, bool suppressFlash, BattlePresentationSprite? actorSprite = null,
        BattleDamageKind? flashKind = null)
    {
        var visible = VisibleAfter(before, after); var values = Array.AsReadOnly(damage.ToArray());
        var marked = Array.AsReadOnly(effectPositions.Distinct().ToArray());
        for (var phase = 0; phase < 10; phase++)
            yield return new()
            {
                Stage = BattlePresentationStage.Damage, ActorSlot = actorSlot, FrameIndex = phase, FrameCount = 10,
                Duration = Delay(1), Before = before, After = after, Combatants = visible, ActorSprite = actorSprite,
                Damage = values, DamagePhase = phase, Flash = !suppressFlash && phase < 4,
                HighlightedCells = marked, FlashKind = flashKind ?? values.FirstOrDefault()?.Kind ?? BattleDamageKind.Hp
            };
    }
}
