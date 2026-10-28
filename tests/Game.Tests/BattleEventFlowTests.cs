using System.Buffers.Binary;
using JinYong.Content.Legacy;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class BattleEventFlowTests
{
    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public void BattleSuspendsTheSameRunnerAndResumesOnlyItsSelectedBranch(bool victory)
    {
        var world = new BattleWorld();
        var root = new EventProgram(1, [new(0, EventOperation.Call, [2]), new(1, EventOperation.End, [])]);
        var child = new EventProgram(2, [new(0, EventOperation.Battle, [0, 10, 20, 1]),
            new(10, EventOperation.SetVariable, [0, 7]), new(11, EventOperation.Return, []),
            new(20, EventOperation.SetVariable, [0, 9]), new(21, EventOperation.Return, [])]);
        var story = new StoryCatalog(70, null, -1, null, Direction.Right, [root, child], [],
            new Dictionary<int, string>(), new Dictionary<int, int>(), variableNames: new Dictionary<int, string> { [0] = "test:battle/result" });
        var runner = new EventRunner(story, world);
        Assert.IsTrue(runner.TryStart(1, 0, new(20, 20)));
        var pending = runner.PendingBattle!;
        Assert.AreEqual((0, 1), world.Started);
        Assert.IsTrue(runner.IsRunning); Assert.IsNull(runner.Pending);
        Assert.IsFalse(runner.Confirm(pending.Token));
        runner.Advance(TimeSpan.FromHours(1));
        Assert.AreSame(pending, runner.PendingBattle); Assert.AreEqual(0, world.Value);
        Assert.IsFalse(runner.ResumeBattle(pending.Token));
        world.Result = victory;
        Assert.IsFalse(runner.ResumeBattle(pending.Token + 1));
        Assert.IsTrue(runner.ResumeBattle(pending.Token));
        Assert.IsFalse(runner.IsRunning); Assert.IsNull(runner.PendingBattle);
        Assert.AreEqual(victory ? 7 : 9, world.Value);
        CollectionAssert.AreEqual(new[] { 2, 1 }, runner.CompletedPrograms.ToArray());
        Assert.IsFalse(runner.ResumeBattle(pending.Token));
        Assert.IsTrue(runner.TryStart(1, 0, new(20, 20)));
        Assert.AreNotEqual(pending.Token, runner.PendingBattle!.Token);
        Assert.IsFalse(runner.ResumeBattle(pending.Token));
    }

    [TestMethod]
    public void DosBattleOffsetsAreRelativeToTheFiveWordInstructionEnd()
    {
        short[] words = [6, 4, 0, 3, 1, 56, 2, -1, 56, 9, -1];
        var bytes = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), words[i]);
        var compiled = LegacyEventCompiler.Compile(1, bytes);
        var command = compiled.Commands.Single(value => value.SourceOpcode == 6);
        Assert.AreEqual(EventOperation.Battle, command.Operation);
        CollectionAssert.AreEqual(new[] { 4, 5, 8, 1 }, command.Arguments);
        var program = new EventProgram(1, compiled.Commands.Select(value => new EventCommand(value.Id, value.Operation, value.Arguments)));
        CollectionAssert.AreEqual(new[] { 5, 8 }, EventProgram.Targets(program.Commands[0]).ToArray());
    }

    private sealed class BattleWorld : IEventWorld
    {
        public (int, int) Started { get; private set; }
        public int Value { get; private set; }
        public bool? Result { get; set; }
        public bool TryGetBattleOutcome(out bool victory) { victory = Result == true; return Result.HasValue; }
        public void BeginBattle(int id, int experienceFlag) => Started = (id, experienceFlag);
        public void ValidateCommand(EventCommand command, int context) { }
        public void SetVariable(int id, int value) => Value = value;
        public int GetVariable(int id) => Value;
        public void ChangeEvent(int context, GridPosition trigger, IReadOnlyList<int> arguments) => throw new NotSupportedException();
        public void Face(Direction direction) => throw new NotSupportedException();
        public void SetPicture(int eventIndex, int picture) => throw new NotSupportedException();
        public void AddItem(int id, int count) => throw new NotSupportedException();
        public int ItemCount(int id) => 0;
        public int EventField(int context, IReadOnlyList<int> arguments) => 0;
        public void SetTile(IReadOnlyList<int> arguments) => throw new NotSupportedException();
        public void Teleport(GridPosition position) => throw new NotSupportedException();
    }
}
