using System.Buffers.Binary;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Session;

namespace JinYong.Importing.Legacy;

public enum LegacyScriptDialect { DosOriginal, Extended50, Unknown }

public static class LegacyEventCompiler
{
    private static readonly int[] Widths =
    [1,4,3,14,4,3,5,1,2,3,2,3,1,1,1,2,4,6,4,3,3,2,1,3,1,5,6,4,6,6,5,4,3,4,
     3,5,4,2,5,2,2,4,3,4,7,3,3,3,3,3,8,1,1,1,1,5,2,1,1,1,6,3,7,3,1,1,2,2];

    public static EventProgramDocument Compile(int id, ReadOnlySpan<byte> bytes, LegacyScriptDialect dialect = LegacyScriptDialect.DosOriginal)
    {
        if (dialect != LegacyScriptDialect.DosOriginal)
            throw new NotSupportedException($"Script profile {dialect} is not supported. Extended 50 must not be interpreted as original opcode 50.");
        if (bytes.Length == 0 || bytes.Length % 2 != 0) throw new InvalidDataException($"KDEF {id} has an invalid word count.");
        var commands = new List<EventCommandDocument>();
        // Traverse control flow, not the first linear stretch ending in -1.
        // Both branches may contain their own terminator and unreachable padding.
        var words = new short[bytes.Length / 2];
        for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]);
        var owners = Enumerable.Repeat(-1, words.Length).ToArray();
        var visited = new HashSet<int>();
        var pending = new Queue<int>([0]);
        while (pending.TryDequeue(out var pc))
        {
            if (pc < 0 || pc >= words.Length) throw new InvalidDataException($"KDEF {id}: target {pc} is outside the script.");
            if (!visited.Add(pc)) continue;
            var opcode = words[pc];
            if (opcode == -1)
            {
                if (owners[pc] != -1 && owners[pc] != pc) throw new InvalidDataException($"KDEF {id}: jump into instruction operands at {pc}.");
                owners[pc] = pc;
                commands.Add(new(pc, EventOperation.End, [], -1));
                continue;
            }
            if (opcode < 0 || opcode >= Widths.Length) throw new InvalidDataException($"KDEF {id}, word {pc}: invalid opcode {opcode}.");
            var width = Widths[opcode];
            if (pc + width > words.Length) throw new InvalidDataException($"KDEF {id}, word {pc}: truncated opcode {opcode}.");
            for (var i = pc; i < pc + width; i++)
            {
                if (owners[i] != -1 && owners[i] != pc) throw new InvalidDataException($"KDEF {id}: overlapping instruction at {pc}.");
                owners[i] = pc;
            }
            var arguments = new int[width - 1];
            for (var i = 0; i < arguments.Length; i++) arguments[i] = words[pc + i + 1];
            int Target(int offset) => checked(pc + width + offset);
            if (opcode == 55)
            {
                // Z.DAT sub_300D9 caller aliases comparison, yes and no to arg2.
                // All 28 original uses jump to their own final zero parameter,
                // which executes one refresh before the ordinary next command.
                var target = Target(arguments[1]);
                commands.Add(new(pc, EventOperation.Jump, [target], opcode));
                if (target == pc + 4 && words[target] == 0)
                {
                    commands.Add(new(target, EventOperation.Refresh, [], opcode));
                    pending.Enqueue(pc + width);
                }
                else pending.Enqueue(target);
                continue;
            }
            string? text = null;
            EventChoice[]? choices = null;
            var operation = opcode switch
            {
                0 => EventOperation.Refresh, 1 => EventOperation.Dialogue, 2 => EventOperation.AddItem,
                3 => EventOperation.ChangeEvent, 27 => EventOperation.Animate, 40 => EventOperation.Face,
                51 => EventOperation.RandomDialogue, 67 => EventOperation.Sound,
                4 => EventOperation.BranchUsedItem, 5 or 9 or 11 => EventOperation.Choice, 7 => EventOperation.Stop,
                17 => EventOperation.SetTile, 18 or 43 => EventOperation.BranchItemExists,
                31 => EventOperation.BranchItem, 19 => EventOperation.Teleport,
                32 => EventOperation.AdjustItem, 50 => EventOperation.BranchAllItems, 60 => EventOperation.BranchEvent,
                10 => EventOperation.JoinParty, 12 => EventOperation.RestParty, 16 => EventOperation.BranchParty,
                20 => EventOperation.BranchPartyFull, 21 => EventOperation.LeaveParty, 22 => EventOperation.ClearPartyMp,
                23 or 49 or 63 => EventOperation.SetRoleAttribute,
                26 => EventOperation.IncrementEventScripts, 28 or 29 or 36 => EventOperation.BranchRole,
                33 => EventOperation.LearnMagic, 34 or 37 or 45 or 46 or 47 or 48 => EventOperation.AddRoleAttribute,
                35 => EventOperation.SetMagic, 38 => EventOperation.ReplaceTiles, 39 => EventOperation.OpenScene,
                41 => EventOperation.RoleAddItem, 42 => EventOperation.BranchPartyFemale,
                52 or 53 => EventOperation.NoticeRole, 54 => EventOperation.OpenAllScenes,
                56 => EventOperation.AddFame, 59 => EventOperation.DismissParty,
                61 => EventOperation.BranchBooks, 65 => EventOperation.MoveShop,
                13 or 14 => EventOperation.Fade,
                25 => EventOperation.PanCamera, 30 => EventOperation.ScriptedWalk, 44 => EventOperation.AnimatePair,
                6 => EventOperation.Battle, 15 => EventOperation.Death, 24 => EventOperation.LoadMenu,
                8 => EventOperation.ExitMusic, 57 => EventOperation.StatueAnimation, 58 => EventOperation.TournamentTrials,
                62 => EventOperation.Ending, 64 => EventOperation.Shop, 66 => EventOperation.Music,
                _ => throw new NotSupportedException($"KDEF {id}, word {pc}: opcode {opcode} is not implemented.")
            };
            if (opcode == 51) arguments = [2547, 18, 114, 0];
            // OpenLegend Z_DAT/0x2E278.md and 0x2E28A.md fix direction and
            // blocking order. Duration is a remake approximation, not DOS frames.
            if (opcode is 13 or 14) arguments = [opcode == 14 ? 1 : 0, 450];
            // Z.DAT sub_2F9F2: the second end word is never read.
            if (opcode is 44 or 62) arguments = arguments.Take(5).ToArray();
            if (opcode == 6) arguments = [arguments[0], Target(arguments[1]), Target(arguments[2]), arguments[3]];
            if (opcode is 5 or 9 or 11)
            {
                text = opcode == 5 ? "是否与之过招？" : opcode == 9 ? "是否邀请加入？" : "是否休息？";
                choices = [new("是", Target(arguments[0])), new("否", Target(arguments[1]))];
                arguments = [choices[1].Target];
            }
            else if (opcode is 4 or 16 or 18 or 31 or 43)
            {
                var yes = Target(arguments[1]); var no = Target(arguments[2]);
                arguments = opcode == 31 ? [174, arguments[0], yes, no] : [arguments[0], yes, no];
            }
            else if (opcode is 20 or 42 or 61) arguments = [Target(arguments[0]), Target(arguments[1])];
            else if (opcode == 28) arguments = [arguments[0], RoleWords.Morality, arguments[1], arguments[2], Target(arguments[3]), Target(arguments[4])];
            // Z.DAT sub_2F136 never reads argument3; there is no attack upper bound.
            else if (opcode == 29) arguments = [arguments[0], RoleWords.Attack, arguments[1], int.MaxValue, Target(arguments[3]), Target(arguments[4])];
            else if (opcode == 36) arguments = [0, RoleWords.Sex, arguments[0], arguments[0], Target(arguments[1]), Target(arguments[2])];
            else if (opcode is 23 or 49 or 63) arguments = [arguments[0], opcode == 23 ? RoleWords.UsePoison : opcode == 49 ? RoleWords.MpType : RoleWords.Sex, arguments[1]];
            else if (opcode is 34 or 45 or 46 or 47 or 48)
            {
                var field = opcode switch { 34 => RoleWords.Iq, 45 => RoleWords.Speed, 46 => RoleWords.MaximumMp,
                    47 => RoleWords.Attack, _ => RoleWords.MaximumHp };
                arguments = [arguments[0], field, arguments[1]];
            }
            else if (opcode == 37) arguments = [0, RoleWords.Morality, arguments[0]];
            else if (opcode is 52 or 53) arguments = [0, opcode == 52 ? RoleWords.Morality : RoleWords.Fame];
            else if (opcode == 50) arguments = [.. arguments.Take(5), Target(arguments[5]), Target(arguments[6])];
            else if (opcode == 60) arguments = [arguments[0], arguments[1], 5, arguments[2], Target(arguments[3]), Target(arguments[4])];
            var command = new EventCommandDocument(pc, operation, arguments, opcode, text, choices);
            commands.Add(command);
            if (operation == EventOperation.Battle)
            {
                pending.Enqueue(arguments[1]); pending.Enqueue(arguments[2]);
            }
            else if (operation is EventOperation.Choice or EventOperation.BranchItem or EventOperation.BranchUsedItem or EventOperation.BranchAllItems or EventOperation.BranchEvent
                or EventOperation.BranchItemExists or EventOperation.BranchParty or EventOperation.BranchPartyFull
                or EventOperation.BranchPartyFemale or EventOperation.BranchRole or EventOperation.BranchBooks)
            {
                var targets = choices is null ? arguments.TakeLast(2) : choices.Select(choice => choice.Target);
                foreach (var target in targets) pending.Enqueue(target);
            }
            // 15/24 transfer ownership to the outer load/quit lifecycle. Their
            // menus never return to the following instruction on success, and
            // declining exit or a failed load stays in the menu.
            else if (operation is not (EventOperation.Stop or EventOperation.Death or EventOperation.LoadMenu or EventOperation.Ending)) pending.Enqueue(pc + width);
        }
        var result = new EventProgramDocument(id, commands.OrderBy(command => command.Id).ToArray());
        try { _ = new EventProgram(id, result.Commands.Select(command => new EventCommand(command.Id, command.Operation,
            command.Arguments, command.SourceOpcode, command.Text, command.Choices))); }
        catch (ArgumentException error) { throw new InvalidDataException($"KDEF {id}: {error.Message}", error); }
        return result;
    }
}
