using JinYong.Core.Maps;

namespace JinYong.Core.Battles;

public enum BattleTargetingAction { None, Move, Magic, UsePoison, Detox, Medicine, ThrowItem }
public sealed record BattleTargetingRequest(BattleTargetingAction Action, int MagicSlot = -1, int ItemId = -1, Direction? Facing = null);
public sealed record BattleTargetSummary(int Slot, int RoleId, string Name, bool IsFriendly, GridPosition Position,
    int Hp, int MaximumHp, int Mp, int MaximumMp, bool IsEffectiveTarget);
public sealed record BattleTargetingPreview(BattleTargetingRequest Request, int? ActorSlot, GridPosition? HoveredCell,
    Direction Facing, bool IsAvailable, bool IsLegal, IReadOnlyList<GridPosition> SelectableCells,
    IReadOnlyList<GridPosition> AffectedCells, IReadOnlyList<GridPosition> Path,
    IReadOnlyList<BattleTargetSummary> Targets, string Message);

/// <summary>
/// Detached selection range plus a read-only hover query. Reuse while IsCurrent;
/// a different actor, action state, occupancy, inventory or definition invalidates
/// it. This view never executes an action or estimates damage by drawing RNG.
/// </summary>
public sealed class BattleTargetingQuery
{
    private readonly BattleSession _battle;
    private readonly Func<bool> _isCurrent;
    private readonly string? _unavailableReason;
    internal BattleTargetingQuery(BattleSession battle, BattleTargetingRequest request, IReadOnlyList<GridPosition> selectable,
        bool available, string? unavailableReason, Func<bool> isCurrent)
    {
        _battle = battle; Request = request; SelectableCells = Array.AsReadOnly(selectable.ToArray());
        IsAvailable = available; _unavailableReason = unavailableReason; _isCurrent = isCurrent;
    }
    public BattleTargetingRequest Request { get; }
    public IReadOnlyList<GridPosition> SelectableCells { get; }
    public bool IsAvailable { get; }
    public bool IsCurrent => _isCurrent();
    public BattleTargetingPreview At(GridPosition? hover = null) => _battle.PreviewTargeting(this, hover,
        IsCurrent ? _unavailableReason : "当前行动已变化，请重新选择。");
}
