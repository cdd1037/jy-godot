using JinYong.Core.Events;
using JinYong.Core.Maps;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    // All legacy commands use the same authoritative state and event runtime as
    // authored MOD commands. Source contracts: OpenLegend src/scene/scene.cpp.
    private PartyState RequireParty() => Party ?? throw new InvalidOperationException("This package has no imported party/role data.");

    bool IEventWorld.TestGameCondition(EventCommand command)
    {
        var args = command.Arguments;
        return command.Operation switch
        {
            EventOperation.BranchRole => RequireParty().GetWord(args[0], args[1]) >= args[2] &&
                RequireParty().GetWord(args[0], args[1]) <= args[3],
            EventOperation.BranchParty => RequireParty().Contains(args[0]),
            EventOperation.BranchPartyFull => RequireParty().IsFull,
            EventOperation.BranchPartyFemale => RequireParty().HasFemale,
            EventOperation.BranchBooks => World.HaveFourteenBooks(),
            _ => throw new InvalidOperationException($"Unknown game condition {command.Operation}.")
        };
    }

    string? IEventWorld.ExecuteGameCommand(EventCommand command, int context, GridPosition trigger, int randomValue)
    {
        var args = command.Arguments;
        string? notice = null;
        switch (command.Operation)
        {
            case EventOperation.JoinParty:
                var joined = RequireParty().Join(args[0]);
                foreach (var item in joined.Items)
                {
                    // Canonical inventory contains positive quantities. Retain the
                    // legacy slot ID separately for original ID-only conditions.
                    if (item.Count > 0) ((IEventWorld)this).AddItem(item.ItemId, item.Count);
                    else _itemIds.Add(item.ItemId);
                }
                if (joined.Joined && joined.Items.Any(item => item.Count > 0))
                    notice = string.Join("\n", joined.Items.Where(item => item.Count > 0)
                        .Select(item => $"获得 {_story!.ItemNames[item.ItemId]} × {item.Count}"));
                break;
            case EventOperation.LeaveParty: RequireParty().Leave(args[0]); break;
            case EventOperation.RestParty: RequireParty().Rest(); break;
            case EventOperation.ClearPartyMp: RequireParty().ClearMp(); break;
            case EventOperation.SetRoleAttribute: RequireParty().SetWord(args[0], args[1], args[2]); break;
            case EventOperation.AddRoleAttribute:
                var gain = RequireParty().AddAttribute(args[0], args[1], args[2]);
                if (gain > 0 && args[1] != RoleWords.Morality &&
                    (args[1] != RoleWords.MaximumHp || RequireParty().Contains(args[0])))
                    notice = $"{RequireParty().Roles[args[0]].Name} {AttributeName(args[1])}增加 {gain}";
                break;
            case EventOperation.LearnMagic:
                RequireParty().LearnMagic(args[0], args[1]);
                if (args[2] == 0) notice = $"{RequireParty().Roles[args[0]].Name} 学会了一门武功。";
                break;
            case EventOperation.SetMagic: RequireParty().SetMagic(args[0], args[1], args[2], args[3]); break;
            case EventOperation.RoleAddItem: RequireParty().AddRoleItem(args[0], args[1], args[2]); break;
            case EventOperation.NoticeRole:
                notice = $"{AttributeName(args[1])}：{RequireParty().GetWord(args[0], args[1])}";
                break;
            case EventOperation.AddFame:
                RequireParty().AddAttribute(0, RoleWords.Fame, args[0]);
                if (RequireParty().GetWord(0, RoleWords.Fame) >= 200 && !_itemIds.Contains(189) &&
                    Enumerable.Range(144, 14).All(_itemIds.Contains))
                    World.ChangeEvent(World.CurrentSceneId, context, trigger,
                        [70, 11, 1, 1, 932, -1, -1, 7968, 7968, 7968, -2, -2, -2]);
                break;
            case EventOperation.DismissParty:
                RequireParty().DismissCompanions();
                World.HideDepartedCompanions();
                break;
            case EventOperation.IncrementEventScripts:
                World.IncrementEventScripts(args[0], context, args[1], args[2], args[3], args[4]); break;
            case EventOperation.ReplaceTiles: World.ReplaceTiles(args[0], (MapLayer)args[1], args[2], args[3]); break;
            case EventOperation.OpenScene: World.OpenEntrance(args[0]); break;
            case EventOperation.OpenAllScenes: World.OpenAllEntrances(); break;
            case EventOperation.MoveShop: World.MoveWeiXiaobaoShop(randomValue); break;
            default: throw new InvalidOperationException($"Unknown game command {command.Operation}.");
        }
        PresentationRevision++;
        return notice;
    }

    private static string AttributeName(int word) => word switch
    {
        RoleWords.Iq => "资质", RoleWords.Speed => "轻功", RoleWords.Attack => "攻击力",
        RoleWords.MaximumHp => "生命上限", RoleWords.MaximumMp => "内力上限",
        RoleWords.Morality => "品德", RoleWords.Fame => "声望", _ => "属性"
    };

    private void ValidateGameCommand(EventCommand command, int context)
    {
        var args = command.Arguments;
        switch (command.Operation)
        {
            case EventOperation.JoinParty: case EventOperation.LeaveParty: case EventOperation.SetRoleAttribute:
            case EventOperation.AddRoleAttribute: case EventOperation.BranchRole: case EventOperation.LearnMagic:
            case EventOperation.SetMagic: case EventOperation.RoleAddItem: case EventOperation.NoticeRole:
            case EventOperation.BranchParty:
                if (!RequireParty().Roles.ContainsKey(args[0])) throw new ArgumentException("Unknown role.");
                break;
            case EventOperation.RestParty: case EventOperation.ClearPartyMp:
            case EventOperation.BranchPartyFull: case EventOperation.BranchPartyFemale:
                _ = RequireParty(); break;
            case EventOperation.AddFame:
                _ = RequireParty().GetWord(0, RoleWords.Fame);
                _ = World.Scene(70); break;
            case EventOperation.DismissParty:
                _ = RequireParty();
                if (_story?.World is null) throw new InvalidOperationException("Dismissal requires imported world data.");
                break;
            case EventOperation.IncrementEventScripts:
                _ = World.EventField(args[0], args[1] == -2 ? context : args[1], 2); break;
            case EventOperation.ReplaceTiles:
                var scene = World.Scene(args[0]);
                scene.ValidateTile((MapLayer)args[1], new(0, 0), args[2]);
                scene.ValidateTile((MapLayer)args[1], new(0, 0), args[3]);
                break;
            case EventOperation.OpenScene:
                if (_story?.World is null) throw new InvalidOperationException("Entrance changes require imported world metadata.");
                _ = World.Scene(args[0]); break;
            case EventOperation.OpenAllScenes:
                if (_story?.World is null) throw new InvalidOperationException("Entrance changes require imported world metadata.");
                break;
            case EventOperation.BranchBooks: _ = World.EventField(-2, 24, 7); break;
            case EventOperation.MoveShop:
                foreach (var id in new[] { 1, 3, 40, 60, 61 }) _ = World.Scene(id);
                break;
        }
    }
}
