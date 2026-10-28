using JinYong.Core.Battles;
using JinYong.Core.Events;

namespace JinYong.Core.Session;

public enum PartyActionKind { Medicine, Detox }

/// <summary>Applied means the attempt was accepted, including a genuine zero-effect result.</summary>
public sealed record PartyActionResult(bool Applied, string Message, int Amount = 0, int HurtHealed = 0);

public sealed partial class GameSession
{
    // Z_DAT/0x25BBA: these are table entries, not a field in the 91-word role record.
    // Entry N dispatches KDEF 950+2*N. Unknown companions are refused rather than
    // emulating the original uninitialized-register path or silently removing them.
    private static readonly int[] CompanionDepartureRoles =
        [1,2,9,16,17,25,28,29,35,36,37,38,44,45,47,48,49,51,53,54,58,59,61,63,76];

    /// <summary>
    /// Original exploration actor filter, in party order: skill >=10, including
    /// the protagonist. No battle-menu skill20/stamina gate is imposed here.
    /// Source: OpenLegend Z_DAT/0x21496.md and 0x21AC0.md.
    /// This query does not spend stamina, advance the session or draw randomness.
    /// </summary>
    public IReadOnlyList<int> GetPartyActionActors(PartyActionKind kind)
    {
        if (Party is not { } party || !Enum.IsDefined(kind)) return Array.Empty<int>();
        var skill = kind == PartyActionKind.Medicine ? RoleWords.Medicine : RoleWords.Detoxification;
        return Array.AsReadOnly(party.Members.Where(id => id >= 0 && party.Roles[id][skill] >= 10).ToArray());
    }

    /// <summary>
    /// Exploration invokes the same original treatment kernels as battle, with
    /// the single session RNG. It does not invoke battle EndAction: medicine's
    /// kernel costs two physical power at >=50, detox costs none. Both may return
    /// zero; detox still consumes its two native draws. Same-role treatment is valid.
    /// Sources: Z_DAT/0x21496, 0x21AC0, 0x39DA3 and historical 0x3A10C contracts;
    /// current OpenLegend C++ NG+ treatment formulas are deliberately not imported.
    /// </summary>
    public PartyActionResult UsePartyAbility(PartyActionKind kind, int actorRoleId, int targetRoleId)
    {
        if (!CanSave) return new(false, "请等到自由行动时再使用队伍能力。");
        if (!Enum.IsDefined(kind)) return new(false, "未知的队伍能力。");
        if (Party is not { } party || Events is not { } events) return new(false, "此资源包没有队伍资料。");
        if (!party.Contains(actorRoleId) || !party.Contains(targetRoleId)) return new(false, "施术者与目标都必须在队伍中。");
        var actor = party.Roles[actorRoleId];
        var target = party.Roles[targetRoleId];
        var skill = kind == PartyActionKind.Medicine ? RoleWords.Medicine : RoleWords.Detoxification;
        if (actor[skill] < 10) return new(false, kind == PartyActionKind.Medicine ? "该队员的医疗能力不足。" : "该队员的解毒能力不足。");

        if (kind == PartyActionKind.Medicine)
        {
            var beforeHurt = target[RoleWords.Hurt];
            var result = BattleSupportRules.Medicine(actor, target, events.DrawRandom);
            // Read the kernel completely before writing, preserving actor==target behavior.
            target.Set(RoleWords.Hp, result.Hp);
            target.Set(RoleWords.Hurt, result.Hurt);
            actor.Set(RoleWords.PhysicalPower, result.PhysicalPower);
            var healed = beforeHurt - result.Hurt;
            PresentationRevision++;
            return new(true, $"{actor.Name}为{target.Name}恢复生命 {result.Amount} 点，减轻伤势 {healed} 点。", result.Amount, healed);
        }
        else
        {
            var result = BattleSupportRules.Detox(actor, target, events.DrawRandom);
            target.Set(RoleWords.Poison, result.Poison);
            PresentationRevision++;
            return new(true, $"{actor.Name}为{target.Name}解除中毒 {result.Amount} 点。", result.Amount);
        }
    }

    /// <summary>
    /// Starts the original leave-party program in this session's existing runner.
    /// Applied denotes successful script dispatch, not immediate removal: dialogue,
    /// party compression, equipment cleanup and home-event changes remain script-owned.
    /// World travel retains its state and cached scene. All original leave programs
    /// name their map targets explicitly (including 990's scene80 condition/scene18 tiles),
    /// so no temporary scene0 or second event interpreter is needed.
    /// </summary>
    public PartyActionResult RequestCompanionDeparture(int roleId)
    {
        if (!CanSave) return new(false, "请等到自由行动时再安排队员离队。");
        if (Party is not { } party || Events is null) return new(false, "此资源包没有队伍资料。");
        if (!party.Contains(roleId)) return new(false, "这位队员不在队伍中。");
        if (roleId == 0) return new(false, "没有主角，江湖之旅无法继续。");
        var index = Array.IndexOf(CompanionDepartureRoles, roleId);
        if (index < 0) return new(false, "这位队员没有已知的原版离队剧情。");
        var scriptId = 950 + 2 * index;
        if (_story is null || !_story.Programs.TryGetValue(scriptId, out var program))
            return new(false, "该队员的离队剧情尚不可用。" + _story?.UnavailablePrograms.GetValueOrDefault(scriptId));
        if (IsWorld && WorldDepartureContextError(program, []) is { } error) return new(false, error);

        if (!StartEvent(scriptId, -1, _position)) return new(false, InteractionMessage ?? "离队剧情无法启动。");
        return new(true, $"正在处理{party.Roles[roleId].Name}的离队剧情。");
    }

    private string? WorldDepartureContextError(EventProgram program, HashSet<int> visited)
    {
        if (!visited.Add(program.Id)) return null;
        foreach (var command in program.Commands)
        {
            // Relative scene references have no world-map meaning. Reject a MOD
            // override before any dialogue/reward/party mutation rather than apply
            // it to the last visited scene by accident. Native 950..998 need none.
            if ((command.Operation is EventOperation.ChangeEvent or EventOperation.BranchEvent or EventOperation.SetTile or
                 EventOperation.ReplaceTiles or EventOperation.IncrementEventScripts or EventOperation.OpenScene) && command.Arguments[0] == -2 ||
                command.Operation is EventOperation.Animate or EventOperation.AnimatePair or EventOperation.StatueAnimation or
                    EventOperation.Ending or EventOperation.Shop or EventOperation.Battle or EventOperation.TournamentTrials or
                    EventOperation.PanCamera or EventOperation.ScriptedWalk or EventOperation.Teleport or EventOperation.BranchBooks)
                return $"离队剧情 {program.Id} 需要小场景上下文，请进入场景后再安排离队。";
            if (command.Operation == EventOperation.Call && _story!.Programs.TryGetValue(command.Arguments[0], out var called) &&
                WorldDepartureContextError(called, visited) is { } error) return error;
        }
        return null;
    }
}
