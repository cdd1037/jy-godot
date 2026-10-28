using JinYong.Core.Session;
using JinYong.Core.Maps;

namespace JinYong.Core.Events;

internal static class EventCatalogValidator
{
    public static void Validate(StoryCatalog catalog)
    {
        foreach (var program in catalog.Programs.Values)
        foreach (var command in program.Commands)
        {
            var args = command.Arguments;
            void Require(bool valid, string message)
            {
                if (!valid) throw new ArgumentException($"Program {program.Id}, command {command.Id}: {message}");
            }
            RoleDefinition? RequireRole(int id)
            {
                Require(id >= 0, "Negative role ID.");
                // A legacy/partial package may have compiled role commands without
                // a RANGER baseline. Runtime preflight refuses those commands.
                if (catalog.Party is null) return null;
                var role = catalog.Party.Roles.FirstOrDefault(candidate => candidate.Id == id);
                Require(role is not null, "Unknown role.");
                return role;
            }
            void RequireReadableWord(int word) => Require(word is 0 or 1 || RoleWords.IsAttribute(word), "Invalid role attribute word.");
            void RequireScene(int id) => Require(id == -2 || id is >= 0 and <= 32766, "Invalid scene ID.");
            switch (command.Operation)
            {
                case EventOperation.Dialogue: Require(catalog.Dialogues.ContainsKey(args[0]), "Unknown dialogue."); break;
                case EventOperation.RandomDialogue:
                    for (var i = 0; i < args[1]; i++) Require(catalog.Dialogues.ContainsKey(checked(args[0] + i)), "Unknown random dialogue.");
                    break;
                case EventOperation.Call: Require(catalog.Programs.ContainsKey(args[0]), "Unknown called program."); break;
                case EventOperation.AddItem: case EventOperation.AdjustItem: case EventOperation.BranchItem: case EventOperation.BranchUsedItem:
                case EventOperation.BranchItemExists:
                    Require(catalog.ItemNames.ContainsKey(args[0]), "Unknown item."); break;
                case EventOperation.BranchAllItems:
                    foreach (var item in args.Take(5)) Require(catalog.ItemNames.ContainsKey(item), "Unknown item.");
                    break;
                case EventOperation.SetVariable: case EventOperation.AddVariable: case EventOperation.BranchVariable:
                    Require(catalog.VariableNames.ContainsKey(args[0]), "Unknown variable."); break;
                case EventOperation.ChangeEvent:
                    foreach (var script in args.Skip(4).Take(3).Where(script => script > 0))
                        Require(catalog.Programs.ContainsKey(script) || catalog.UnavailablePrograms.ContainsKey(script), "Unknown replacement event program.");
                    break;
                case EventOperation.JoinParty:
                    var recruit = RequireRole(args[0]);
                    if (recruit is not null)
                        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++)
                        {
                            var item = recruit.Words[RoleWords.TakingItemBegin + slot];
                            Require(item == -1 || catalog.ItemNames.ContainsKey(item), "Recruit carries an unknown item.");
                        }
                    break;
                case EventOperation.LeaveParty: case EventOperation.BranchParty:
                    RequireRole(args[0]); break;
                case EventOperation.SetRoleAttribute: case EventOperation.AddRoleAttribute:
                    RequireRole(args[0]);
                    Require(RoleWords.IsAttribute(args[1]), "Role identity/name words cannot be written.");
                    break;
                case EventOperation.BranchRole:
                    RequireRole(args[0]); RequireReadableWord(args[1]);
                    Require(args[2] <= args[3], "Role attribute range is reversed.");
                    break;
                case EventOperation.NoticeRole:
                    RequireRole(args[0]); RequireReadableWord(args[1]); break;
                case EventOperation.LearnMagic:
                    RequireRole(args[0]); Require(args[1] >= 0, "Negative magic ID."); break;
                case EventOperation.SetMagic:
                    RequireRole(args[0]);
                    Require(args[1] is >= -1 and < RoleWords.MagicCount, "Invalid magic slot.");
                    Require(args[2] >= 0 && args[3] >= 0, "Negative magic ID or level.");
                    break;
                case EventOperation.RoleAddItem:
                    RequireRole(args[0]); Require(catalog.ItemNames.ContainsKey(args[1]), "Unknown carried item."); break;
                case EventOperation.IncrementEventScripts:
                    RequireScene(args[0]); Require(args[1] == -2 || args[1] is >= 0 and < 200, "Invalid event index."); break;
                case EventOperation.ReplaceTiles:
                    RequireScene(args[0]); Require(args[1] is >= 0 and < SceneDefinition.LayerCount, "Invalid map layer.");
                    Require(args[2] is >= short.MinValue and <= short.MaxValue && args[3] is >= short.MinValue and <= short.MaxValue,
                        "Replacement tile values must fit a signed 16-bit word.");
                    break;
                case EventOperation.OpenScene:
                    Require(args[0] is >= 0 and <= 32766, "Invalid scene ID."); break;
            }
        }
        // Calls are reusable subroutines, not recursive programs. Loops use jumps
        // and are guarded by the runner's execution budgets.
        var depths = new Dictionary<int, int>();
        var visiting = new HashSet<int>();
        int Visit(int id)
        {
            if (depths.TryGetValue(id, out var known)) return known;
            if (!visiting.Add(id)) throw new ArgumentException($"Recursive event call involving program {id}.");
            if (visiting.Count > 33) throw new ArgumentException("Event call depth exceeded 32.");
            var depth = 1;
            foreach (var command in catalog.Programs[id].Commands.Where(command => command.Operation == EventOperation.Call))
                depth = Math.Max(depth, 1 + Visit(command.Arguments[0]));
            if (depth > 33) throw new ArgumentException("Event call depth exceeded 32.");
            visiting.Remove(id);
            depths[id] = depth;
            return depth;
        }
        foreach (var id in catalog.Programs.Keys) Visit(id);
    }
}
