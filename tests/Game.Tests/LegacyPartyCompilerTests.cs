using System.Buffers.Binary;
using JinYong.Content.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class LegacyPartyCompilerTests
{
    [TestMethod]
    public void PartyAndWorldOpcodesConsumeTheirExactOriginalWidths()
    {
        // Independently authored operand fixtures; no original script data.
        (int Opcode, int[] Args, EventOperation Operation)[] cases =
        [
            (10, [1], EventOperation.JoinParty), (12, [], EventOperation.RestParty),
            (16, [1,0,0], EventOperation.BranchParty), (18, [7,0,0], EventOperation.BranchItemExists),
            (20, [0,0], EventOperation.BranchPartyFull), (21, [1], EventOperation.LeaveParty),
            (22, [], EventOperation.ClearPartyMp), (23, [1,50], EventOperation.SetRoleAttribute),
            (26, [-2,1,0,1,0], EventOperation.IncrementEventScripts), (28, [0,10,80,0,0], EventOperation.BranchRole),
            (29, [0,90,1000,0,0], EventOperation.BranchRole), (33, [1,5,0], EventOperation.LearnMagic),
            (34, [1,3], EventOperation.AddRoleAttribute), (35, [1,-1,5,100], EventOperation.SetMagic),
            (36, [1,0,0], EventOperation.BranchRole), (37, [3], EventOperation.AddRoleAttribute),
            (38, [-2,1,100,200], EventOperation.ReplaceTiles), (39, [3], EventOperation.OpenScene),
            (41, [1,7,3], EventOperation.RoleAddItem), (42, [0,0], EventOperation.BranchPartyFemale),
            (43, [7,0,0], EventOperation.BranchItemExists), (45, [1,2], EventOperation.AddRoleAttribute),
            (46, [1,2], EventOperation.AddRoleAttribute), (47, [1,2], EventOperation.AddRoleAttribute),
            (48, [1,2], EventOperation.AddRoleAttribute), (49, [1,2], EventOperation.SetRoleAttribute),
            (52, [], EventOperation.NoticeRole), (53, [], EventOperation.NoticeRole),
            (54, [], EventOperation.OpenAllScenes), (56, [2], EventOperation.AddFame),
            (59, [], EventOperation.DismissParty), (61, [0,0], EventOperation.BranchBooks),
            (63, [1,2], EventOperation.SetRoleAttribute), (65, [], EventOperation.MoveShop)
        ];
        foreach (var test in cases)
        {
            var program = Compile([test.Opcode, .. test.Args, -1]);
            Assert.AreEqual(2, program.Commands.Length, $"opcode {test.Opcode}");
            Assert.AreEqual(test.Operation, program.Commands[0].Operation, $"opcode {test.Opcode}");
            Assert.AreEqual(test.Opcode, program.Commands[0].SourceOpcode);
            Assert.AreEqual(1 + test.Args.Length, program.Commands[1].Id, $"opcode {test.Opcode} width");
            Assert.AreEqual(EventOperation.End, program.Commands[1].Operation);
        }
    }

    [TestMethod]
    public void RoleArgumentsUseOriginalWordOffsetsAndIgnoreAttackMaximum()
    {
        CollectionAssert.AreEqual(new[] { 0, RoleWords.Attack, 90, int.MaxValue, 6, 6 }, First(29,0,90,-32768,0,0));
        CollectionAssert.AreEqual(First(29,0,90,-32768,0,0), First(29,0,90,32767,0,0));
        CollectionAssert.AreEqual(new[] { 0, RoleWords.Morality, 10, 90, 6, 6 }, First(28,0,10,90,0,0));
        CollectionAssert.AreEqual(new[] { 0, RoleWords.Sex, 2, 2, 4, 4 }, First(36,2,0,0));
        CollectionAssert.AreEqual(new[] { 1, RoleWords.UsePoison, 80 }, First(23,1,80));
        CollectionAssert.AreEqual(new[] { 1, RoleWords.MpType, 2 }, First(49,1,2));
        CollectionAssert.AreEqual(new[] { 1, RoleWords.Sex, 2 }, First(63,1,2));
        CollectionAssert.AreEqual(new[] { 0, RoleWords.Morality, -5 }, First(37,-5));
        foreach (var pair in new[] { (34, RoleWords.Iq), (45, RoleWords.Speed), (46, RoleWords.MaximumMp),
            (47, RoleWords.Attack), (48, RoleWords.MaximumHp) })
            CollectionAssert.AreEqual(new[] { 1, pair.Item2, 5 }, First(pair.Item1,1,5));
        CollectionAssert.AreEqual(new[] { 0, RoleWords.Morality }, First(52));
        CollectionAssert.AreEqual(new[] { 0, RoleWords.Fame }, First(53));
    }

    [TestMethod]
    public void NewConditionsTranslateOffsetsRelativeToCompleteInstructionWidth()
    {
        foreach (var words in new[]
        {
            new[] {16,2,1,0,-1,-1}, new[] {18,7,1,0,-1,-1}, new[] {43,7,1,0,-1,-1},
            new[] {20,1,0,-1,-1}, new[] {42,1,0,-1,-1}, new[] {61,1,0,-1,-1},
            new[] {28,0,0,100,1,0,-1,-1}, new[] {29,0,90,1000,1,0,-1,-1},
            new[] {36,1,1,0,-1,-1}
        })
        {
            var program = Compile(words);
            var branch = program.Commands[0];
            CollectionAssert.AreEqual(new[] { words.Length - 1, words.Length - 2 }, branch.Arguments.TakeLast(2).ToArray());
            CollectionAssert.AreEqual(new[] { words.Length - 2, words.Length - 1 }, program.Commands.Skip(1).Select(command => command.Id).ToArray());
        }
    }

    [TestMethod]
    public void Opcode55UsesCallerAliasAndExecutesItsZeroOperandAsOneRefresh()
    {
        var program = Compile(55,2,-1,14,0,-1);
        CollectionAssert.AreEqual(new[] { EventOperation.Jump, EventOperation.Refresh, EventOperation.End },
            program.Commands.Select(command => command.Operation).ToArray());
        CollectionAssert.AreEqual(new[] { 0,4,5 }, program.Commands.Select(command => command.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 4 }, program.Commands[0].Arguments);
        // arg3/arg4 are not false/true offsets; with arg2=0 it goes to the next instruction.
        var noAlias = Compile(55,2,0,30000,-30000,-1);
        Assert.AreEqual(2, noAlias.Commands.Length);
        CollectionAssert.AreEqual(new[] { 5 }, noAlias.Commands[0].Arguments);
    }

    [TestMethod]
    public void Opcode55ExceptionDoesNotPermitOrdinaryBranchesIntoOperands()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Compile(4,0,-1,0,-1));
        Assert.ThrowsExactly<InvalidDataException>(() => Compile(55,2,-1,14,1,-1));
        Assert.ThrowsExactly<InvalidDataException>(() => Compile(4,0,0,1,55,0,-1,14,0,-1));
    }

    [TestMethod]
    public void NonDosDialectsCannotBeMisreadAsFiveItemTest()
    {
        var bytes = Bytes(50,1,2,3,4,5,0,0,-1);
        Assert.ThrowsExactly<NotSupportedException>(() => LegacyEventCompiler.Compile(1, bytes, LegacyScriptDialect.Extended50));
        Assert.ThrowsExactly<NotSupportedException>(() => LegacyEventCompiler.Compile(1, bytes, LegacyScriptDialect.Unknown));
        Assert.AreEqual(EventOperation.BranchAllItems, LegacyEventCompiler.Compile(1, bytes).Commands[0].Operation);
    }

    [TestMethod]
    public void InstructionsWithoutRealBackendsAreStillRejected()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => Compile(8,1,-1));
        Assert.ThrowsExactly<NotSupportedException>(() => Compile(62,0,2,4,1,6,8,-1));
    }

    private static int[] First(params int[] words) => Compile([.. words, -1]).Commands[0].Arguments;
    private static EventProgramDocument Compile(params int[] words) => LegacyEventCompiler.Compile(1, Bytes(words));
    private static byte[] Bytes(params int[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), checked((short)words[index]));
        return bytes;
    }
}
