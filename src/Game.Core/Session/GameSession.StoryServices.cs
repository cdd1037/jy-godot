using JinYong.Core.Events;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    public EventEndingRequest? PendingEnding => Events?.PendingEnding;

    private void ValidateStoryServices(EventCommand command)
    {
        if (command.Operation == EventOperation.StatueAnimation)
            foreach (var index in new[] { 2, 3, 4 }) _ = World.EventField(-2, index, 5);
        if (command.Operation == EventOperation.Ending)
            foreach (var index in new[] { command.Arguments[0], command.Arguments[3] }.Where(index => index != -1))
                _ = World.EventField(-2, index, 5);
        if (command.Operation == EventOperation.TournamentTrials)
        {
            if (Party?.Roles.ContainsKey(0) != true || BattleResources?.Progression is null ||
                Enumerable.Range(102, 30).Any(id => !BattleResources.Definitions.ContainsKey(id)))
                throw new InvalidOperationException("武林大会需要完整的人物、战斗和战后成长资料。");
            for (var index = 24; index <= 72; index++) _ = World.EventField(-2, index, 5);
        }
        if (command.Operation == EventOperation.Music && command.Arguments[0] is < 0 or > 23)
            throw new InvalidOperationException("音乐编号必须对应原版的24首曲目。");
        if (command.Operation == EventOperation.ExitMusic && command.Arguments[0] > 23)
            throw new InvalidOperationException("离场音乐编号必须对应原版曲目，负数表示使用场景默认音乐。");
    }

    void IEventWorld.RestTournamentProtagonist()
    {
        var protagonist = Party?.Roles[0] ?? throw new InvalidOperationException("The protagonist is unavailable.");
        // Z_DAT/0x30480: this differs from opcode12; no companions are restored.
        if (protagonist[RoleWords.Hurt] >= 50 || protagonist[RoleWords.Poison] != 0) return;
        protagonist.Set(RoleWords.Hurt, 0);
        protagonist.Set(RoleWords.PhysicalPower, 100);
        protagonist.Set(RoleWords.Hp, protagonist[RoleWords.MaximumHp]);
        protagonist.Set(RoleWords.Mp, protagonist[RoleWords.MaximumMp]);
        PresentationRevision++;
    }

    void IEventWorld.BeginEnding()
    {
        if (IsRetired || IsWorld || Battle is not null || PendingTravel is not null)
            throw new InvalidOperationException("The ending cannot start in the current mode.");
        _arrivalPending = false;
        _resolveAfterEvent = false;
        PresentationRevision++;
    }
}
