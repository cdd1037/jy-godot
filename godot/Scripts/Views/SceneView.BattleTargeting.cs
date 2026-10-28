using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class SceneView
{
    private BattleTargetingRequest? _battleTargeting;
    private BattleTargetingQuery? _battleTargetQuery;
    private BattleTargetingPreview? _battleTargetPreview;
    private BattleSession? _targetingOwnerBattle;
    private BattleCombatant? _targetingOwnerActor;
    private int _targetingOwnerRound = -1;
    private GridPosition? _hoveredBattleCell, _queriedBattleCell;
    private bool _hasQueriedBattleCell;
    private HashSet<GridPosition> _battleSelectableCells = [], _battleAffectedCells = [], _battlePathCells = [], _battleEffectiveTargetCells = [];

    public event Action<BattleTargetingPreview?>? BattlePreviewChanged;
    public BattleTargetingPreview? BattlePreview => _battleTargetPreview;
    public BattleTargetingRequest? BattleTargeting
    {
        get => _battleTargeting;
        set
        {
            if (!SynchronizeBattleTargetingOwner()) return;
            if (_battleTargeting == value) { RefreshBattleTargeting(); return; }
            _battleTargeting = value; _battleMovePreview = value?.Action == BattleTargetingAction.Move;
            if (value is null) { _hoveredBattleCell = null; _selectedBattleCell = null; }
            _battleTargetQuery = null; _hasQueriedBattleCell = false;
            RefreshBattleTargeting(); QueueRedraw();
        }
    }

    private bool SynchronizeBattleTargetingOwner()
    {
        var battle = _session?.Battle; var actor = battle?.CurrentActor;
        var changed = !ReferenceEquals(battle, _targetingOwnerBattle) || !ReferenceEquals(actor, _targetingOwnerActor) ||
            (battle?.Round ?? -1) != _targetingOwnerRound;
        var selectable = battle is { IsPresenting: false, Outcome: BattleOutcome.Ongoing, Phase: BattlePhase.Acting } && actor is { IsFriendly: true };
        _targetingOwnerBattle = battle; _targetingOwnerActor = actor; _targetingOwnerRound = battle?.Round ?? -1;
        if (changed || !selectable) ClearBattleTargeting();
        return selectable;
    }
    private void ClearBattleTargeting()
    {
        var hadPreview = _battleTargetPreview is not null;
        _battleTargeting = null; _battleTargetQuery = null; _battleTargetPreview = null; _battleMovePreview = false;
        _hoveredBattleCell = null; _queriedBattleCell = null; _hasQueriedBattleCell = false; _selectedBattleCell = null;
        _battleSelectableCells.Clear(); _battleAffectedCells.Clear(); _battlePathCells.Clear(); _battleEffectiveTargetCells.Clear();
        if (hadPreview) { QueueRedraw(); BattlePreviewChanged?.Invoke(null); }
    }
    private void RefreshBattleTargeting()
    {
        if (!SynchronizeBattleTargetingOwner()) return;
        if (_battleTargeting is null)
        {
            if (_battleTargetPreview is not null)
            {
                _battleTargetPreview = null; _battleTargetQuery = null;
                _battleSelectableCells.Clear(); _battleAffectedCells.Clear(); _battlePathCells.Clear(); _battleEffectiveTargetCells.Clear();
                BattlePreviewChanged?.Invoke(null);
            }
            return;
        }
        if (_battleTargetQuery is null || !_battleTargetQuery.IsCurrent)
        {
            _battleTargetQuery = _targetingOwnerBattle!.CreateTargetingQuery(_battleTargeting);
            _battleSelectableCells = _battleTargetQuery.SelectableCells.ToHashSet(); _hasQueriedBattleCell = false;
        }
        if (_hasQueriedBattleCell && _queriedBattleCell == _hoveredBattleCell) return;
        _queriedBattleCell = _hoveredBattleCell; _hasQueriedBattleCell = true;
        _battleTargetPreview = _battleTargetQuery.At(_hoveredBattleCell);
        _battleAffectedCells = _battleTargetPreview.AffectedCells.ToHashSet(); _battlePathCells = _battleTargetPreview.Path.ToHashSet();
        _battleEffectiveTargetCells = _battleTargetPreview.Targets.Where(target => target.IsEffectiveTarget).Select(target => target.Position).ToHashSet();
        QueueRedraw(); BattlePreviewChanged?.Invoke(_battleTargetPreview);
    }
    private GridPosition? BattleCellAt(Vector2 screenPosition)
    {
        if (_session?.Battle is not { } battle) return null;
        var point = (screenPosition - BattleOrigin(battle)) / _zoom;
        var x = (point.X / 18 + point.Y / 9) / 2; var y = (point.Y / 9 - point.X / 18) / 2;
        var cell = new GridPosition((int)Math.Round(x, MidpointRounding.AwayFromZero), (int)Math.Round(y, MidpointRounding.AwayFromZero));
        return BattlefieldDefinition.Contains(cell) ? cell : null;
    }
    private bool TryHandleBattleHover(InputEvent input)
    {
        if (_session?.Battle is null || input is not InputEventMouseMotion mouse) return false;
        UpdateBattleHover(mouse.Position);
        return true;
    }
    private void UpdateBattleHover(Vector2 screenPosition)
    {
        if (!SynchronizeBattleTargetingOwner()) return;
        var cell = BattleCellAt(screenPosition);
        if (cell != _hoveredBattleCell)
        { _hoveredBattleCell = cell; RefreshBattleTargeting(); QueueRedraw(); }
    }
    private void ClearBattleHover()
    {
        if (_hoveredBattleCell is null) return;
        _hoveredBattleCell = null; RefreshBattleTargeting(); QueueRedraw();
    }

    private void DrawBattleTargetingCell(GridPosition cell, Action<GridPosition, Color, Color> diamond)
    {
        if (_battleTargetPreview is not { } preview) return;
        if (_battleSelectableCells.Contains(cell)) diamond(cell, new Color(0.15f, 0.55f, 0.7f, 0.12f), new Color(0.3f, 0.7f, 0.85f, 0.3f));
        if (_battleAffectedCells.Contains(cell)) diamond(cell, new Color(0.95f, 0.55f, 0.15f, 0.2f), new Color(1, 0.7f, 0.3f, 0.7f));
        if (_battlePathCells.Contains(cell)) diamond(cell, new Color(0.2f, 0.9f, 0.8f, 0.2f), new Color(0.4f, 0.95f, 0.85f, 0.7f));
        if (_battleEffectiveTargetCells.Contains(cell)) diamond(cell, new Color(0.35f, 0.95f, 0.5f, 0.25f), new Color(0.55f, 1, 0.65f, 0.95f));
        if (preview.HoveredCell == cell)
        {
            var color = preview.IsLegal ? new Color(0.55f, 1, 0.75f, 0.95f) : new Color(1, 0.35f, 0.3f, 0.95f);
            diamond(cell, new Color(color.R, color.G, color.B, 0.14f), color);
        }
    }
    private void DrawBattleTargetingPath(Vector2 origin)
    {
        if (_battleTargetPreview?.Path is not { Count: > 1 } path) return;
        DrawPolyline(path.Select(cell => origin + BattleProject(cell) * _zoom).ToArray(), new Color(0.4f, 0.95f, 0.85f, 0.8f), Math.Max(1, _zoom));
    }
}
