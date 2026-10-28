using System.Text.Json;
using JinYong.Content.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class EventFlowTests
{
    private static EventCommand C(int id, EventOperation op, params int[] args) => new(id, op, args);
    private static EventCommand Question(int id, int cancel, params EventChoice[] choices) => new(id, EventOperation.Choice,
        [cancel], text: "请选择", choices: choices);
    private static StoryCatalog Story(EventProgram[] programs, int? initial = null, Dictionary<int, int>? bag = null) =>
        new(70, initial, 0, null, Direction.Right, programs, [new(10, ["第一段"]), new(11, ["第二段"])],
            new Dictionary<int, string> { [0] = "物品甲", [1] = "物品乙", [174] = "银两" }, bag ?? [],
            variableNames: new Dictionary<int, string> { [0] = "test:quest/progress" });

    [TestMethod]
    public void ChoiceRequiresAnExplicitValidAnswerAndCannotBeAnsweredTwice()
    {
        var program = new EventProgram(1,
        [Question(0, 40, new("领取", 10), new("不领", 20)), C(10, EventOperation.AddItem, 1, 1), C(11, EventOperation.End),
            C(20, EventOperation.AddItem, 1, 2), C(21, EventOperation.End), C(40, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([program], 1));
        var events = session.Events!;
        var token = events.Pending!.Token;
        Assert.AreEqual(EventPresentationKind.Choice, events.Pending.Kind);
        Assert.IsFalse(events.Confirm(token));
        Assert.IsFalse(events.Choose(token, 9));
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Down).Block);
        Assert.IsTrue(events.Choose(token, 0));
        Assert.AreEqual(1, session.Inventory[1]);
        Assert.IsFalse(events.Choose(token, 1));
        Assert.IsFalse(events.Confirm(token));
        events.Confirm(events.Pending!.Token);
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(1, session.Inventory[1]);
    }

    [TestMethod]
    public void ChoiceCancellationFollowsItsOwnBranchAndNonCancelableChoicesStayOpen()
    {
        EventProgram Program(int cancel) => new(1, [Question(0, cancel, new("甲", 10), new("乙", 10)),
            C(10, EventOperation.SetVariable, 0, 1), C(11, EventOperation.End), C(20, EventOperation.End)]);
        var blocked = new GameSession(MovementTests.Scene(), Story([Program(-1)], 1));
        Assert.IsFalse(blocked.Events!.CancelChoice(blocked.Events.Pending!.Token));
        Assert.IsTrue(blocked.IsEventRunning);
        var session = new GameSession(MovementTests.Scene(), Story([Program(20)], 1));
        Assert.IsTrue(session.Events!.CancelChoice(session.Events.Pending!.Token));
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(0, session.Variables.Count);
    }

    [TestMethod]
    public void VariablesLoopsCallsAndWaitsResumeAtTheCorrectCommand()
    {
        var root = new EventProgram(1, [C(0, EventOperation.SetVariable, 0, 0), C(1, EventOperation.Jump, 2),
            C(2, EventOperation.BranchVariable, 0, (int)EventComparison.GreaterOrEqual, 3, 7, 3),
            C(3, EventOperation.Call, 2), C(4, EventOperation.Wait, 10), C(5, EventOperation.Jump, 2),
            C(7, EventOperation.Dialogue, 10, 0, 0), C(8, EventOperation.End)]);
        var child = new EventProgram(2, [C(0, EventOperation.AddVariable, 0, 1), C(1, EventOperation.Return)]);
        var session = new GameSession(MovementTests.Scene(), Story([root, child], 1));
        Assert.AreEqual(1, session.Variables["test:quest/progress"]);
        session.Events!.Advance(TimeSpan.FromMilliseconds(9));
        Assert.AreEqual(1, session.Variables["test:quest/progress"]);
        session.Events.Advance(TimeSpan.FromMilliseconds(1));
        Assert.AreEqual(2, session.Variables["test:quest/progress"]);
        session.Events.Advance(TimeSpan.FromMilliseconds(10));
        session.Events.Advance(TimeSpan.FromMilliseconds(10));
        Assert.AreEqual(3, session.Variables["test:quest/progress"]);
        Assert.AreEqual(10, session.Events.Pending!.DialogueId);
        Assert.AreEqual(1, session.Events.ProgramId);
        session.Events.Confirm(session.Events.Pending.Token);
        CollectionAssert.AreEqual(new[] { 2, 2, 2, 1 }, session.Events.CompletedPrograms.ToArray());
        Assert.IsTrue(session.Events.Trace.Any(entry => entry.Program == 2));
    }

    [TestMethod]
    public void ItemUseIsDistinctFromTalkingAndDoesNotConsumeAnItemImplicitly()
    {
        var layers = MovementTests.EmptyLayers();
        layers[(int)MapLayer.Event * 4096 + 20 * 64 + 20] = 0;
        var words = new short[11]; words[0] = 1; words[2] = 1; words[3] = 2; words[9] = words[10] = 20;
        var points = Enumerable.Range(0, 200).Select(_ => new SceneEventDefinition(words)).ToArray();
        var talk = new EventProgram(1, [C(0, EventOperation.Dialogue, 10, 0, 0), C(1, EventOperation.End)]);
        var item = new EventProgram(2, [C(0, EventOperation.BranchUsedItem, 1, 10, 20),
            C(10, EventOperation.SetVariable, 0, 1), C(11, EventOperation.End), C(20, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(layers, points), Story([talk, item], bag: new() { [0] = 1, [1] = 1 }));
        Assert.IsFalse(session.UseItem(174));
        Assert.IsTrue(session.UseItem(0));
        Assert.AreEqual(0, session.Variables.Count);
        Assert.IsTrue(session.UseItem(1));
        Assert.AreEqual(1, session.Variables["test:quest/progress"]);
        Assert.AreEqual(1, session.Inventory[1]);
        Assert.IsTrue(session.Interact());
        Assert.AreEqual(10, session.Events!.Pending!.DialogueId);
        Assert.IsFalse(session.UseItem(1));
    }

    [TestMethod]
    public void PreflightChecksCalledProgramsBeforeAnyRewardsAreCommitted()
    {
        var root = new EventProgram(1, [C(0, EventOperation.AdjustItem, 1, 100), C(1, EventOperation.Call, 2), C(2, EventOperation.End)]);
        var child = new EventProgram(2, [C(0, EventOperation.SetTile, 99, 0, 20, 20, 0), C(1, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([root, child]));
        Assert.IsFalse(session.Events!.TryStart(1, 0, session.Player.Position));
        StringAssert.Contains(session.Events.Error!, "target scene is not available");
        Assert.AreEqual(0, session.Inventory.Count);
        Assert.AreEqual(0, session.Events.Trace.Count);
        Assert.IsFalse(session.IsEventRunning);
    }

    [TestMethod]
    public void InfiniteJumpStopsWithAUsefulTraceAndReleasesTheSessionAfterAcknowledgement()
    {
        var loop = new EventProgram(1, [C(0, EventOperation.Jump, 0), C(1, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([loop], 1));
        Assert.IsNotNull(session.Events!.Error);
        StringAssert.Contains(session.Events.Error, "command 0");
        Assert.AreEqual(64, session.Events.Trace.Count);
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Right).Block);
        Assert.IsTrue(session.Events.Confirm(session.Events.Pending!.Token));
        Assert.IsFalse(session.IsEventRunning);
        Assert.IsTrue(session.TryMove(Direction.Right).Moved);
    }

    [TestMethod]
    public void InvalidTargetsReferencesVariablesAndRecursiveCallsAreRejectedAtLoad()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EventProgram(1, [C(0, EventOperation.Jump, 99), C(1, EventOperation.End)]));
        Assert.ThrowsExactly<ArgumentException>(() => Story([new(1, [C(0, EventOperation.Call, 9), C(1, EventOperation.End)])]));
        Assert.ThrowsExactly<ArgumentException>(() => Story([new(1, [C(0, EventOperation.Call, 1), C(1, EventOperation.End)])]));
        Assert.ThrowsExactly<ArgumentException>(() => Story([new(1, [C(0, EventOperation.SetVariable, 99, 0), C(1, EventOperation.End)])]));
        Assert.ThrowsExactly<ArgumentException>(() => Story([new(1, [C(0, EventOperation.Dialogue, 999, 0, 0), C(1, EventOperation.End)])]));
    }

    [TestMethod]
    public void TileChangesAndTeleportUpdateTheRuntimeAndPresentationWithoutChangingDefinitions()
    {
        var root = new EventProgram(1, [C(0, EventOperation.SetTile, -2, 0, 11, 10, 8),
            C(1, EventOperation.Teleport, 10, 10), C(2, EventOperation.End)]);
        var definition = MovementTests.Scene();
        var session = new GameSession(definition, Story([root]));
        var walker = new SceneWalker(session);
        session.Events!.TryStart(1, 0, session.Player.Position);
        walker.Advance(0);
        Assert.AreEqual(new GridPosition(10, 10), walker.To);
        Assert.AreEqual(walker.From, walker.To);
        Assert.AreEqual((short)8, session.Scene.At(MapLayer.Ground, new(11, 10)));
        Assert.AreEqual((short)0, definition.At(MapLayer.Ground, new(11, 10)));
    }

    [TestMethod]
    [DataRow(0, 11)] [DataRow(1, 10)]
    public void LegacyConditionalCanJumpPastAnEarlierEndAndExecutesTheCorrectBranch(int count, int dialogue)
    {
        var raw = new short[] { 18,1,0,5, 1,10,0,0, -1, 1,11,0,0, -1 };
        var compiled = LegacyEventCompiler.Compile(1, raw.SelectMany(BitConverter.GetBytes).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 4, 8, 9, 13 }, compiled.Commands.Select(command => command.Id).ToArray());
        var program = new EventProgram(1, compiled.Commands.Select(command => new EventCommand(command.Id, command.Operation,
            command.Arguments, command.SourceOpcode, command.Text, command.Choices)));
        var session = new GameSession(MovementTests.Scene(), Story([program], 1, count == 0 ? [] : new() { [1] = count }));
        Assert.AreEqual(dialogue, session.Events!.Pending!.DialogueId);
    }

    [TestMethod]
    public void LegacyCompilerRejectsOperandJumpsAndIgnoresUnreachableUnsupportedPadding()
    {
        byte[] Bytes(params short[] words) => words.SelectMany(BitConverter.GetBytes).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyEventCompiler.Compile(1, Bytes(18,1,-3,0,-1)));
        var stopped = LegacyEventCompiler.Compile(1, Bytes(7,12));
        Assert.AreEqual(1, stopped.Commands.Length);
        Assert.AreEqual(EventOperation.Stop, stopped.Commands[0].Operation);
        var question = LegacyEventCompiler.Compile(1, Bytes(11,0,1,-1,-1));
        Assert.AreEqual(EventOperation.Choice, question.Commands[0].Operation);
        CollectionAssert.AreEqual(new[] { 3,4 }, question.Commands[0].Choices!.Select(choice => choice.Target).ToArray());
    }

    [TestMethod]
    public void JsonRoundTripRetainsChoiceTargetsAndNamespacedVariables()
    {
        var document = new StoryDocument
        {
            SceneId = 70, VariableNames = new() { [0] = "test:quest/progress" },
            Programs = [new(1, [new(0, EventOperation.Choice, [-1], Text: "选择", Choices: [new("甲", 1), new("乙", 1)]),
                new(1, EventOperation.SetVariable, [0, 2]), new(2, EventOperation.End, [])])]
        };
        var json = JsonSerializer.Serialize(document, ContentJson.Options);
        var loaded = JsonSerializer.Deserialize<StoryDocument>(json, ContentJson.Options)!.ToCatalog();
        var session = new GameSession(MovementTests.Scene(), loaded);
        session.Events!.TryStart(1, 0, session.Player.Position);
        session.Events.Choose(session.Events.Pending!.Token, 1);
        Assert.AreEqual(2, session.Variables["test:quest/progress"]);
    }

    [TestMethod]
    [DataRow(EventComparison.Equal, 4, true)] [DataRow(EventComparison.NotEqual, 4, false)]
    [DataRow(EventComparison.Less, 5, true)] [DataRow(EventComparison.LessOrEqual, 4, true)]
    [DataRow(EventComparison.Greater, 5, false)] [DataRow(EventComparison.GreaterOrEqual, 4, true)]
    public void VariableComparisonsUseTheSelectedOperator(EventComparison comparison, int right, bool expected)
    {
        var root = new EventProgram(1, [C(0, EventOperation.SetVariable, 0, 4),
            C(1, EventOperation.BranchVariable, 0, (int)comparison, right, 2, 4),
            C(2, EventOperation.Dialogue, 10, 0, 0), C(3, EventOperation.End),
            C(4, EventOperation.Dialogue, 11, 0, 0), C(5, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Story([root], 1));
        Assert.AreEqual(expected ? 10 : 11, session.Events!.Pending!.DialogueId);
    }

    [TestMethod]
    public void EventFieldAndAllItemConditionsReadTheCurrentWorld()
    {
        var root = new EventProgram(1, [C(0, EventOperation.BranchAllItems, 0, 1, 0, 1, 0, 2, 5),
            C(2, EventOperation.BranchEvent, -2, -2, 0, 0, 3, 5),
            C(3, EventOperation.Dialogue, 10, 0, 0), C(4, EventOperation.End),
            C(5, EventOperation.Dialogue, 11, 0, 0), C(6, EventOperation.End)]);
        var yes = new GameSession(MovementTests.Scene(), Story([root], 1, new() { [0] = 1, [1] = 1 }));
        Assert.AreEqual(10, yes.Events!.Pending!.DialogueId);
        var no = new GameSession(MovementTests.Scene(), Story([root], 1, new() { [0] = 1 }));
        Assert.AreEqual(11, no.Events!.Pending!.DialogueId);
    }

    [TestMethod]
    public void StopInACalledProgramDoesNotResumeItsCaller()
    {
        var root = new EventProgram(1, [C(0, EventOperation.Call, 2), C(1, EventOperation.AdjustItem, 1, 100), C(2, EventOperation.End)]);
        var child = new EventProgram(2, [C(0, EventOperation.Stop)]);
        var session = new GameSession(MovementTests.Scene(), Story([root, child], 1));
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(0, session.Inventory.Count);
    }

    [TestMethod]
    public void ACommandCanKeepTheCoordinateAssignedByAnEarlierCommand()
    {
        var words = new short[11]; words[9] = words[10] = -1;
        var points = Enumerable.Range(0, 200).Select(_ => new SceneEventDefinition(words)).ToArray();
        var root = new EventProgram(1,
        [C(0, EventOperation.ChangeEvent, -2, 0, -2, -2, -2, -2, -2, -2, -2, -2, -2, 20, 20),
            C(1, EventOperation.ChangeEvent, -2, 0, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, 21),
            C(2, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(events: points), Story([root]));
        Assert.IsTrue(session.Events!.TryStart(1, 0, session.Player.Position));
        Assert.IsNull(session.Events.Error);
        Assert.AreEqual((short)-1, session.Scene.At(MapLayer.Event, new(20, 20)));
        Assert.AreEqual((short)0, session.Scene.At(MapLayer.Event, new(20, 21)));
    }
}
