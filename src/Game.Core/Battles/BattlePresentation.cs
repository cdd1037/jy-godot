using JinYong.Core.Items;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public enum BattlePresentationStage { Movement, Fight, EffectPrelude, Effect, Damage, Item }
public enum BattleDamageKind { Hp = 1, Poison = 2, Detox = 3, Medicine = 4, Mp = 5 }
public enum BattleSoundBank { Attack, Effect }
public enum BattleSoundAction { Load, StartLoaded }
public sealed record BattlePresentationSound(BattleSoundBank Bank, int Id, BattleSoundAction Action);
public sealed record BattlePresentationSprite(string Bank, int Frame, GridPosition Position);
public sealed record BattlePresentationDamage(int TargetSlot, GridPosition Position, int Amount, BattleDamageKind Kind);
public sealed record BattleMovementFrame(GridPosition From, GridPosition To, double Progress);

/// <summary>A detached immutable role/slot view; never an authoritative RoleState reference.</summary>
public sealed record BattleCombatantSnapshot
{
    internal BattleCombatantSnapshot(BattleCombatant combatant)
    {
        Slot = combatant.Slot; RoleId = combatant.RoleId; Name = combatant.Name; IsFriendly = combatant.IsFriendly;
        Position = combatant.Position; Facing = combatant.Facing; Hidden = combatant.Hidden;
        Words = Array.AsReadOnly(Enumerable.Range(0, RoleWords.Count).Select(word => combatant.Role[word]).ToArray());
    }
    public int Slot { get; init; }
    public int RoleId { get; }
    public string Name { get; }
    public bool IsFriendly { get; }
    public GridPosition Position { get; init; }
    public Direction Facing { get; init; }
    public bool Hidden { get; init; }
    public IReadOnlyList<int> Words { get; }
    public int HeadId => Words[RoleWords.HeadId];
    public int Hp => Words[RoleWords.Hp];
    public int MaximumHp => Words[RoleWords.MaximumHp];
    public int Mp => Words[RoleWords.Mp];
    public int Poison => Words[RoleWords.Poison];
    public int Hurt => Words[RoleWords.Hurt];
    public int SpriteId => 5106 + 8 * HeadId + 2 * (int)Facing;
}

/// <summary>
/// One immutable rendered frame. Before/After are the action's detached states;
/// Combatants chooses the visible state and retains hit victims until damage ends.
/// ActorSprite remains the final FIGHT sprite throughout its damage frames.
/// Missing bank/frame is a content error; consumers must not substitute raw indices
/// from another bank. All identifiers already use the library's doubled IDs.
/// </summary>
public sealed record BattlePresentationFrame
{
    public required BattlePresentationStage Stage { get; init; }
    public required int ActorSlot { get; init; }
    public required int FrameIndex { get; init; }
    public required int FrameCount { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyList<BattleCombatantSnapshot> Before { get; init; }
    public required IReadOnlyList<BattleCombatantSnapshot> After { get; init; }
    public required IReadOnlyList<BattleCombatantSnapshot> Combatants { get; init; }
    public long Token { get; init; }
    public bool WaitingForConfirmation { get; init; }
    public BattlePresentationSprite? ActorSprite { get; init; }
    public IReadOnlyList<BattlePresentationSprite> Effects { get; init; } = Array.Empty<BattlePresentationSprite>();
    public IReadOnlyList<BattlePresentationDamage> Damage { get; init; } = Array.Empty<BattlePresentationDamage>();
    public IReadOnlyList<BattlePresentationSound> Sounds { get; init; } = Array.Empty<BattlePresentationSound>();
    public bool Flash { get; init; }
    public BattleDamageKind FlashKind { get; init; } = BattleDamageKind.Hp;
    public IReadOnlyList<GridPosition> HighlightedCells { get; init; } = Array.Empty<GridPosition>();
    public int DamagePhase { get; init; }
    public BattleMovementFrame? Movement { get; init; }
    public ItemUseResult? ItemUse { get; init; }
}

public sealed class BattlePresentationPlan
{
    internal BattlePresentationPlan(IEnumerable<BattlePresentationFrame> frames,
        IEnumerable<BattlePresentationSound>? initialSounds = null)
    {
        Frames = Array.AsReadOnly(frames.ToArray());
        InitialSounds = Array.AsReadOnly((initialSounds ?? []).ToArray());
    }
    public IReadOnlyList<BattlePresentationFrame> Frames { get; }
    public IReadOnlyList<BattlePresentationSound> InitialSounds { get; }
}
