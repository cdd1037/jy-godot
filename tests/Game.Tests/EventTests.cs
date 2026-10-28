using System.Text;
using JinYong.Content.Legacy;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class EventTests
{
    private static EventCommand Command(int id, EventOperation operation, params int[] args) => new(id, operation, args);
    private static StoryCatalog Catalog(EventProgram[] programs, DialogueDefinition[]? dialogues = null,
        int? initial = 1, IReadOnlyDictionary<int, int>? inventory = null) => new(70, initial, 0, 6890, Direction.Right,
            programs, dialogues ?? [new(10, ["第一页", "第二页"])], new Dictionary<int, string> { [174] = "銀兩" },
            inventory ?? new Dictionary<int, int>());

    [TestMethod]
    public void DialogueRequiresMatchingTokenAndPreventsMovement()
    {
        var program = new EventProgram(1, [Command(0, EventOperation.Dialogue, 10, 0, 1), Command(1, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Catalog([program]));
        var first = session.Events!.Pending!;
        Assert.AreEqual("第一页", first.Text);
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Down).Block);
        Assert.AreEqual(Direction.Right, session.Player.Facing);
        Assert.IsTrue(session.Events.Confirm(first.Token));
        Assert.AreEqual("第二页", session.Events.Pending!.Text);
        Assert.IsFalse(session.Events.Confirm(first.Token));
        Assert.IsTrue(session.IsEventRunning);
        session.Events.Confirm(session.Events.Pending.Token);
        Assert.IsFalse(session.IsEventRunning);
        CollectionAssert.AreEqual(new[] { 1 }, session.Events.CompletedPrograms.ToArray());
    }

    [TestMethod]
    public void AnimationWaitsTwoBiosTicksAndFacingClearsPlayerOverride()
    {
        var program = new EventProgram(1,
        [Command(0, EventOperation.Animate, -1, 10, 14), Command(1, EventOperation.Face, 0), Command(2, EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(), Catalog([program]));
        Assert.AreEqual(10, session.Player.PictureOverride);
        session.Events!.Advance(EventRunner.AnimationFrameTime - TimeSpan.FromTicks(1));
        Assert.AreEqual(10, session.Player.PictureOverride);
        session.Events.Advance(TimeSpan.FromTicks(1));
        Assert.AreEqual(12, session.Player.PictureOverride);
        session.Events.Advance(EventRunner.AnimationFrameTime);
        Assert.AreEqual(14, session.Player.PictureOverride);
        Assert.IsTrue(session.IsEventRunning);
        session.Events.Advance(EventRunner.AnimationFrameTime);
        Assert.IsFalse(session.IsEventRunning);
        Assert.IsNull(session.Player.PictureOverride);
        Assert.AreEqual(Direction.Up, session.Player.Facing);
    }

    [TestMethod]
    public void ChestChangesRuntimeEventAndRewardsOnlyOnce()
    {
        var layers = MovementTests.EmptyLayers();
        layers[3 * 4096 + 20 * 64 + 20] = 0;
        var words = new short[11]; words[0] = 1; words[2] = 1; words[9] = 20; words[10] = 20;
        var events = Enumerable.Range(0, 200).Select(_ => new SceneEventDefinition(words)).ToArray();
        var definition = MovementTests.Scene(layers, events);
        var program = new EventProgram(1,
        [Command(0, EventOperation.ChangeEvent, -2,-2,-2,-2,-1,-1,-1,3500,3500,3500,-2,-2,-2),
         Command(1, EventOperation.AddItem,174,100), Command(2,EventOperation.End)]);
        var session = new GameSession(definition, Catalog([program], initial: null, inventory: new Dictionary<int, int> { [174] = 50 }));
        Assert.IsTrue(session.Interact()); // Starts facing right at (19,20).
        Assert.AreEqual(150, session.Inventory[174]);
        Assert.AreEqual(3500, session.Scene.Events[0].Picture);
        Assert.AreEqual((short)-1, session.Scene.Events[0][2]);
        Assert.IsFalse(session.Interact());
        Assert.AreEqual(1, session.Definition.Events[0][2]);
        Assert.AreEqual(EventPresentationKind.Item, session.Events!.Pending!.Kind);
        Drain(session);
        Assert.IsFalse(session.Interact());
        Assert.AreEqual(150, session.Inventory[174]);
    }

    [TestMethod]
    public void ArrivalEventWaitsForMovementCompletionAndCannotRetriggerAfterDisabling()
    {
        var layers = MovementTests.EmptyLayers(); layers[3 * 4096 + 20 * 64 + 20] = 0;
        var words = new short[11]; words[4] = 1; words[9] = 20; words[10] = 20;
        var events = Enumerable.Range(0, 200).Select(_ => new SceneEventDefinition(words)).ToArray();
        var program = new EventProgram(1,
        [Command(0,EventOperation.Dialogue,10,0,1),
         Command(1,EventOperation.ChangeEvent,-2,-2,-2,-2,-2,-2,-1,-2,-2,-2,-2,-2,-2), Command(2,EventOperation.End)]);
        var session = new GameSession(MovementTests.Scene(layers, events), Catalog([program], initial: null));
        Assert.IsTrue(session.TryMove(Direction.Right).Moved);
        Assert.IsFalse(session.IsEventRunning);
        session.CompleteMovement();
        Assert.IsTrue(session.IsEventRunning);
        Drain(session);
        session.TryMove(Direction.Left); session.CompleteMovement();
        session.TryMove(Direction.Right); session.CompleteMovement();
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(1, session.Events!.CompletedCount);
    }

    [TestMethod]
    public void SoundQueueIsConsumedOnceAndRandomDialogueUsesLegacyRng()
    {
        var program = new EventProgram(1,
        [Command(0,EventOperation.Sound,24), Command(1,EventOperation.RandomDialogue,2547,18,114,0), Command(2,EventOperation.End)]);
        var dialogues = Enumerable.Range(2547, 18).Select(id => new DialogueDefinition(id, [$"#{id}"])).ToArray();
        var session = new GameSession(MovementTests.Scene(), Catalog([program], dialogues));
        CollectionAssert.AreEqual(new[] { 24 }, session.Events!.TakeSounds());
        Assert.IsEmpty(session.Events.TakeSounds());
        Assert.AreEqual(2555, session.Events.Pending!.DialogueId); // seed=1 -> 16838 % 18 = 8
    }

    [TestMethod]
    public void LegacyTextPreservesThreeLinePagesAndIntentionalEmptyPage()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(950).GetBytes("一*二*三*四");
        var encoded = text.Select(value => (byte)(value ^ 255)).Append((byte)0).ToArray();
        CollectionAssert.AreEqual(new[] { "一\n二\n三", "四" }, LegacyTextDecoder.DecodeDialogue(encoded));
        CollectionAssert.AreEqual(new[] { "", "" }, LegacyTextDecoder.DecodeDialogue([213,213,213,0]));
    }

    [TestMethod]
    public void CompilerKeepsWordOffsetsAndRejectsUnsupportedOrTruncatedPrograms()
    {
        byte[] Bytes(params short[] words) => words.SelectMany(BitConverter.GetBytes).ToArray();
        var program = LegacyEventCompiler.Compile(691, Bytes(1,2520,0,1,0,40,0,-1));
        CollectionAssert.AreEqual(new[] { 0, 4, 5, 7 }, program.Commands.Select(command => command.Id).ToArray());
        Assert.AreEqual(EventOperation.Dialogue, program.Commands[0].Operation);
        Assert.AreEqual(1, program.Commands[0].SourceOpcode);
        Assert.AreEqual(EventOperation.RestParty, LegacyEventCompiler.Compile(931, Bytes(12,-1)).Commands[0].Operation);
        Assert.ThrowsExactly<NotSupportedException>(() => LegacyEventCompiler.Compile(1, Bytes(8,0,-1)));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyEventCompiler.Compile(1, Bytes(1,2520)));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyEventCompiler.Compile(1, Bytes(40,0)));
        Assert.ThrowsExactly<ArgumentException>(() => Command(1,EventOperation.ChangeEvent,0));
    }

    internal static List<int> Drain(GameSession session)
    {
        var dialogues = new List<int>();
        for (var i = 0; i < 4000 && session.IsEventRunning; i++)
        {
            Assert.IsNull(session.Events!.Error);
            if (session.Events.Pending is { } pending)
            {
                if (pending.Kind == EventPresentationKind.Dialogue) dialogues.Add(pending.DialogueId);
                session.Events.Confirm(pending.Token);
            }
            else session.Events.Advance(EventRunner.AnimationFrameTime);
        }
        Assert.IsFalse(session.IsEventRunning, session.Events?.Error ?? "Event did not terminate.");
        return dialogues;
    }
}
