using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public enum BattlePhase { PartySelection, Acting, Finished }
public enum BattleOutcome { Ongoing, Defeat, Victory }

/// <summary>One transient battle slot. Friendly roles remain the session's authoritative PartyState objects.</summary>
public sealed class BattleCombatant
{
    internal BattleCombatant(int slot, RoleState role, bool friendly, GridPosition position)
    { Slot = slot; Role = role; IsFriendly = friendly; Position = position; Facing = friendly ? Direction.Left : Direction.Right; }
    public int Slot { get; internal set; }
    public RoleState Role { get; }
    public int RoleId => Role.Id;
    public string Name => Role.Name;
    public bool IsFriendly { get; }
    public int Side => IsFriendly ? 0 : 1;
    public GridPosition Position { get; internal set; }
    public Direction Facing { get; internal set; }
    public int RoundValue { get; internal set; }
    public bool Hidden { get; internal set; }
    public bool ActionDone { get; internal set; }
    public int RewardExperience { get; internal set; }
    public int Hp => Role[RoleWords.Hp];
    public int MaximumHp => Role[RoleWords.MaximumHp];
    public int Mp => Role[RoleWords.Mp];
    public int PhysicalPower => Role[RoleWords.PhysicalPower];
    public bool IsAlive => !Hidden && Hp > 0;
    public int SpriteId => 5106 + 8 * Role[RoleWords.HeadId] + 2 * (int)Facing;
}

public sealed record BattleDamageEvent(int ActorSlot, int TargetSlot, GridPosition Position, int Damage, bool MpDamage);
