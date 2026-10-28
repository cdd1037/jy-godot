using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

public sealed partial class BattleSession
{
    public BattleTargetingPreview QueryTargeting(BattleTargetingRequest request, GridPosition? hover = null) => CreateTargetingQuery(request).At(hover);

    public BattleTargetingQuery CreateTargetingQuery(BattleTargetingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = CurrentActor; var round = Round; var phase = Phase; var roundValue = actor?.RoundValue;
        var snapshots = CaptureCombatantSnapshots();
        var inventory = _battleInventory?.Items.ToArray() ?? [];
        var magics = _magics.ToArray(); var items = _items.ToArray();
        bool Current() => !IsPresenting && Outcome == BattleOutcome.Ongoing && Phase == phase && Round == round &&
            ReferenceEquals(CurrentActor, actor) && actor?.RoundValue == roundValue && _combatants.Count == snapshots.Count &&
            _combatants.Zip(snapshots).All(pair => pair.First.Slot == pair.Second.Slot && pair.First.RoleId == pair.Second.RoleId &&
                pair.First.Position == pair.Second.Position && pair.First.Facing == pair.Second.Facing && pair.First.Hidden == pair.Second.Hidden &&
                Enumerable.Range(0, RoleWords.Count).All(word => pair.First.Role[word] == pair.Second.Words[word])) &&
            SameEntries(_battleInventory?.Items, inventory) && SameDefinitions(_magics, magics) && SameDefinitions(_items, items);
        BattleTargetingQuery Result(IReadOnlyList<GridPosition> range, bool available, string? reason = null) => new(this, request, range, available, reason, Current);
        if (IsPresenting || Outcome != BattleOutcome.Ongoing || Phase != BattlePhase.Acting || actor is not { IsFriendly: true })
            return Result([], false, "当前不能选择玩家行动目标。");
        if (!Enum.IsDefined(request.Action) || request.Facing is { } facing && !Enum.IsDefined(facing))
            return Result([], false, "目标选择参数无效。");
        try
        {
            switch (request.Action)
            {
                case BattleTargetingAction.Move:
                    var movement = ReachableMoves().Where(pair => pair.Value > 0).Select(pair => pair.Key).ToArray();
                    return Result(movement, movement.Length > 0, movement.Length == 0 ? "当前没有可到达的移动格。" : null);
                case BattleTargetingAction.Magic:
                    if (!AvailableMagicSlots().Contains(request.MagicSlot)) return Result([], false, "当前不能使用所选武功。");
                    var magic = _magics[actor.Role[RoleWords.MagicIdBegin + request.MagicSlot]];
                    ValidateMagicPresentation(actor, magic);
                    var profile = BattleMagicRules.Profile(actor.Role, magic, request.MagicSlot);
                    // Original line/cross cursors are unrestricted. Other magic
                    // shares one upper-layer distance map with the action gate.
                    var magicDistances = profile.AreaType is 1 or 2 ? null : TargetingDistances(actor, profile.SelectDistance);
                    var magicCandidates = magicDistances?.Keys ?? Enumerable.Range(0, BattlefieldDefinition.TileCount)
                        .Select(index => new GridPosition(index % BattlefieldDefinition.Extent, index / BattlefieldDefinition.Extent));
                    return Result(magicCandidates.Where(point => TargetInRange(actor, profile, point, magicDistances)).ToArray(), true);
                case BattleTargetingAction.UsePoison: case BattleTargetingAction.Detox: case BattleTargetingAction.Medicine:
                    var support = SupportAction(request.Action);
                    if (!CanUseSupport(support)) return Result([], false, "当前不满足这项行动的使用条件。");
                    ValidateSupportPresentation(actor, support);
                    var supportDistances = TargetingDistances(actor, SupportRange(support));
                    return Result(supportDistances.Keys.Where(point => TargetSupportInRange(support, point, enforceMenu: true,
                        existingDistances: supportDistances)).ToArray(), true);
                case BattleTargetingAction.ThrowItem:
                    // Only an occupied cell can pass the existing thrown-item
                    // gate. Query those candidates instead of 4096 empty cells.
                    var throwRange = ThrownTargetingRange(actor);
                    if (throwRange < 0) return Result([], false, "所选暗器当前没有可投掷目标。");
                    var throwDistances = TargetingDistances(actor, throwRange);
                    var throwing = Combatants.Select(person => person.Position).Distinct()
                        .Where(point => TargetThrownItemInRange(request.ItemId, point, automatic: false, existingDistances: throwDistances)).ToArray();
                    if (throwing.Length > 0) ValidateThrownItemPresentation(_items[request.ItemId]);
                    return Result(throwing, throwing.Length > 0, throwing.Length == 0 ? "所选暗器当前没有可投掷目标。" : null);
                default: return Result([], false, "请选择一项战斗行动。");
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { return Result([], false, $"无法预览该行动：{error.Message}"); }
    }

    // Existing actions and cached previews call the same pure geometry helper.
    // A preview supplies this map back to the original gate instead of rebuilding
    // it for every candidate. Eligibility/side/inventory checks stay in that gate.
    private IReadOnlyDictionary<GridPosition, int> TargetingDistances(BattleCombatant actor, int maximum) =>
        BattlePathing.Distances(Battlefield, Combatants, actor.Position, targeting: true, maximum: maximum);
    private static int ThrownTargetingRange(BattleCombatant actor) => actor.Role[RoleWords.HiddenWeapon] / 15 + 1;
    private static bool SameEntries(IReadOnlyDictionary<int, int>? current, IReadOnlyList<KeyValuePair<int, int>> saved) =>
        (current?.Count ?? 0) == saved.Count && saved.All(pair => current is not null && current.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private static bool SameDefinitions<T>(IReadOnlyDictionary<int, T> current, IReadOnlyList<KeyValuePair<int, T>> saved) where T : class =>
        current.Count == saved.Count && saved.All(pair => current.TryGetValue(pair.Key, out var value) && ReferenceEquals(value, pair.Value));
    private static BattleSupportAction SupportAction(BattleTargetingAction action) => action switch
    {
        BattleTargetingAction.UsePoison => BattleSupportAction.UsePoison, BattleTargetingAction.Detox => BattleSupportAction.Detox,
        BattleTargetingAction.Medicine => BattleSupportAction.Medicine, _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    internal BattleTargetingPreview PreviewTargeting(BattleTargetingQuery query, GridPosition? hover, string? unavailableReason)
    {
        var request = query.Request; var actor = CurrentActor;
        var direction = request.Facing ?? (actor is not null && hover is { } cell ? BattleMagicRules.Facing(actor.Position, cell) : actor?.Facing ?? Direction.Up);
        IReadOnlyList<GridPosition> affected = Array.Empty<GridPosition>(), path = Array.Empty<GridPosition>();
        var legal = false; var message = unavailableReason;
        try
        {
            if (message is null && query.IsAvailable && hover is { } target && BattlefieldDefinition.Contains(target) && actor is { IsFriendly: true })
            {
                switch (request.Action)
                {
                    case BattleTargetingAction.Move:
                        legal = query.SelectableCells.Contains(target);
                        if (legal) path = Array.AsReadOnly((BattlePathing.FindPath(Battlefield, Combatants, actor.Position, target, actor.RoundValue) ?? []).ToArray());
                        if (legal) affected = Array.AsReadOnly(new[] { target });
                        break;
                    case BattleTargetingAction.Magic:
                        legal = CanTargetMagic(request.MagicSlot, target);
                        if (legal) affected = Array.AsReadOnly(AttackArea(request.MagicSlot, target, direction).Select(cell => cell.Position).Distinct().ToArray());
                        break;
                    case BattleTargetingAction.UsePoison: case BattleTargetingAction.Detox: case BattleTargetingAction.Medicine:
                        legal = CanTargetSupport(SupportAction(request.Action), target);
                        if (legal) affected = Array.AsReadOnly(new[] { target });
                        break;
                    case BattleTargetingAction.ThrowItem:
                        legal = CanTargetThrownItem(request.ItemId, target);
                        if (legal) affected = Array.AsReadOnly(new[] { target });
                        break;
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { message = $"无法预览该目标：{error.Message}"; legal = false; affected = []; path = []; }
        var positions = affected.Concat(hover is { } point ? new[] { point } : []).ToHashSet();
        // Match the existing occupancy map, including its last-slot rule for
        // duplicate coordinates; do not claim every stacked role will be hit.
        var targets = positions.Select(OccupantAt).Where(person => person is not null).Select(person => person!)
            .DistinctBy(person => person.Slot).OrderBy(person => person.Slot).Select(person =>
        {
            var matchingSide = actor is not null && (request.Action is BattleTargetingAction.Detox or BattleTargetingAction.Medicine
                ? person.IsFriendly == actor.IsFriendly : person.IsFriendly != actor.IsFriendly);
            var effective = legal && affected.Contains(person.Position) && matchingSide && request.Action != BattleTargetingAction.Move;
            return new BattleTargetSummary(person.Slot, person.RoleId, person.Name, person.IsFriendly, person.Position,
                person.Hp, person.MaximumHp, person.Mp, person.Role[RoleWords.MaximumMp], effective);
        }).ToArray();
        message ??= hover is null ? request.Action == BattleTargetingAction.Move ? "选择移动目的地。" : "将鼠标移到目标格查看范围。" :
            !legal ? "此格不在当前行动的可选目标范围内。" : request.Action == BattleTargetingAction.Move ? $"移动 {Math.Max(0, path.Count - 1)} 格。" :
            targets.Any(person => person.IsEffectiveTarget) ? $"可影响 {targets.Count(person => person.IsEffectiveTarget)} 个目标。" :
            request.Action is BattleTargetingAction.UsePoison or BattleTargetingAction.Detox or BattleTargetingAction.Medicine
                ? "此格可以确认，但没有对应目标，仍会消耗本次行动。" : "此处可以施放，影响范围内没有敌人。";
        return new(request, actor?.Slot, hover, direction, query.IsAvailable && unavailableReason is null, legal,
            unavailableReason is null ? query.SelectableCells : Array.Empty<GridPosition>(), affected, path, Array.AsReadOnly(targets), message);
    }
}
