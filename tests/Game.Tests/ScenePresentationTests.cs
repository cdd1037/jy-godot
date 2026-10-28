using System.Buffers.Binary;
using JinYong.Importing.Legacy;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class ScenePresentationTests
{
    [TestMethod]
    public void CompilerPreservesMotionWidthsAndIgnoresSecondAnimationEnd()
    {
        var words = new short[] { 25,41,31,34,31,30,28,24,28,19,44,1,10,14,2,20,-30000,-1 };
        var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), words[index]);
        var result = LegacyEventCompiler.Compile(1, bytes);
        CollectionAssert.AreEqual(new[] {0,5,10,17}, result.Commands.Select(command => command.Id).ToArray());
        CollectionAssert.AreEqual(new[] {EventOperation.PanCamera,EventOperation.ScriptedWalk,EventOperation.AnimatePair,EventOperation.End},
            result.Commands.Select(command => command.Operation).ToArray());
        CollectionAssert.AreEqual(new[] {1,10,14,2,20}, result.Commands[2].Arguments);
    }

    [TestMethod]
    public void CameraPanUsesSourceBeforeTargetExcludesEndpointAndNeverMovesPlayer()
    {
        var session = Session(Command(EventOperation.PanCamera,41,31,34,31));
        var start = session.Player;
        for (var coordinate = 41; coordinate >= 35; coordinate--)
        {
            Assert.AreEqual(new CameraView(coordinate,20), session.Camera);
            Assert.AreEqual(start, session.Player);
            Assert.IsTrue(session.IsEventRunning);
            session.Events!.Advance(EventRunner.AnimationFrameTime);
        }
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(new CameraView(35,20), session.Camera);
        Assert.AreEqual(start, session.Player);
    }

    [TestMethod]
    public void CameraPanFinishesXBeforeYAndWrapsWordBeforeClamping()
    {
        var session = Session(Command(EventOperation.PanCamera,20,20,22,22));
        CollectionAssert.AreEqual(new[] {new CameraView(20,20),new CameraView(21,20),new CameraView(21,20),new CameraView(21,21)},
            Enumerable.Range(0,4).Select(_ =>
            {
                var camera = session.Camera!.Value;
                session.Events!.Advance(EventRunner.AnimationFrameTime);
                return camera;
            }).ToArray());
        Assert.IsFalse(session.IsEventRunning);
        var wrapped = Session(Command(EventOperation.PanCamera,-32768,-32768,-32767,-32767));
        Assert.AreEqual(new CameraView(47,20), wrapped.Camera);
        wrapped.Events!.Advance(EventRunner.AnimationFrameTime);
        Assert.AreEqual(new CameraView(47,47), wrapped.Camera);
        wrapped.Events.Advance(EventRunner.AnimationFrameTime);
        Assert.IsFalse(wrapped.IsEventRunning);
    }

    [TestMethod]
    public void CameraPanWaitsForWholeFrameAndZeroDistanceProducesNoFrame()
    {
        var session = Session(Command(EventOperation.PanCamera,20,20,21,20));
        session.Events!.Advance(EventRunner.AnimationFrameTime - TimeSpan.FromTicks(1));
        Assert.IsTrue(session.IsEventRunning);
        session.Events.Advance(TimeSpan.FromTicks(1));
        Assert.IsFalse(session.IsEventRunning);
        var zero = Session(Command(EventOperation.PanCamera,20,20,20,20));
        Assert.IsFalse(zero.IsEventRunning);
        Assert.IsNull(zero.Camera);
    }

    [TestMethod]
    [DataRow(2,2,Direction.Down)]
    [DataRow(2,-2,Direction.Up)]
    [DataRow(-2,2,Direction.Down)]
    [DataRow(-2,-2,Direction.Up)]
    public void ScriptedWalkUsesCounterDistanceFromActualPositionAndXBeforeY(int dx, int dy, Direction facing)
    {
        var session = Session(Command(EventOperation.ScriptedWalk,10,10,10+dx,10+dy));
        Assert.AreEqual(new GridPosition(19+Math.Sign(dx),20), session.Player.Position);
        Assert.AreEqual(2, session.Player.WalkFrameOffset);
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Down).Block);
        session.Events!.Advance(EventRunner.ScriptedWalkFrameTime);
        Assert.AreEqual(new GridPosition(19+dx,20), session.Player.Position);
        session.Events.Advance(EventRunner.ScriptedWalkFrameTime);
        Assert.AreEqual(new GridPosition(19+dx,20+Math.Sign(dy)), session.Player.Position);
        session.Events.Advance(EventRunner.ScriptedWalkFrameTime);
        Assert.AreEqual(new GridPosition(19+dx,20+dy), session.Player.Position);
        Assert.AreEqual(8, session.Player.WalkFrameOffset);
        Assert.AreEqual(facing, session.Player.Facing);
        session.Events.Advance(EventRunner.ScriptedWalkFrameTime);
        Assert.IsTrue(session.IsEventRunning); // Separate final standing refresh.
        Assert.AreEqual(0, session.Player.WalkFrameOffset);
        Assert.IsNull(session.Player.PictureOverride);
        Assert.IsNull(session.ScriptedMotion);
        session.Events.Advance(TimeSpan.Zero);
        Assert.IsFalse(session.IsEventRunning);
        Assert.AreEqual(0L, session.Player.Steps);
    }

    [TestMethod]
    public void BlockedScriptedStepsStillConsumeFramesAndDoNotTriggerArrivalPrograms()
    {
        var layers = MovementTests.EmptyLayers();
        layers[(int)MapLayer.Building*4096+20*64+20] = 4;
        layers[(int)MapLayer.Event*4096+22*64+19] = 0;
        var events = Enumerable.Range(0,200).Select(_ => new SceneEventDefinition(new short[11])).ToArray();
        var words = new short[11]; words[4] = 2; words[9] = 19; words[10] = 22;
        events[0] = new(words);
        var program = Program(Command(EventOperation.ScriptedWalk,0,0,2,2));
        var session = new GameSession(MovementTests.Scene(layers,events), Catalog([program,
            new EventProgram(2,[new(0,EventOperation.Sound,[99]),new(1,EventOperation.End,[])])]));
        Assert.AreEqual(new GridPosition(19,20), session.Player.Position);
        Assert.AreEqual(2, session.Player.WalkFrameOffset);
        Assert.AreEqual(session.ScriptedMotion!.Value.From, session.ScriptedMotion.Value.To);
        session.Events!.Advance(EventRunner.ScriptedWalkFrameTime);
        Assert.AreEqual(4, session.Player.WalkFrameOffset);
        Assert.AreEqual(new GridPosition(19,20), session.Player.Position);
        session.Events.Advance(EventRunner.ScriptedWalkFrameTime);
        Assert.AreEqual(new GridPosition(19,21), session.Player.Position);
        session.Events.Advance(EventRunner.ScriptedWalkFrameTime);
        Assert.AreEqual(new GridPosition(19,22), session.Player.Position);
        session.Events.Advance(EventRunner.ScriptedWalkFrameTime);
        session.Events.Advance(TimeSpan.Zero);
        session.CompleteMovement();
        Assert.IsFalse(session.IsEventRunning);
        CollectionAssert.AreEqual(new[] {1},session.Events.CompletedPrograms.ToArray());
        Assert.IsEmpty(session.Events.TakeSounds());
    }

    [TestMethod]
    public void ScriptedMotionInterpolatesDuringTheThreeTickFrameAndZeroWalkStillRefreshes()
    {
        var session = Session(Command(EventOperation.ScriptedWalk,0,0,1,0));
        Assert.AreEqual(new ScriptedMotionView(new(19,20),new(20,20),0),session.ScriptedMotion);
        var half = TimeSpan.FromTicks(EventRunner.ScriptedWalkFrameTime.Ticks/2);
        session.Events!.Advance(half);
        Assert.AreEqual(half.TotalSeconds/EventRunner.ScriptedWalkFrameTime.TotalSeconds,session.ScriptedMotion!.Value.Progress,1e-12);
        session.Events.Advance(EventRunner.ScriptedWalkFrameTime-half);
        Assert.IsNull(session.ScriptedMotion);
        Assert.AreEqual(0,session.Player.WalkFrameOffset);
        Assert.IsTrue(session.IsEventRunning);
        session.Events.Advance(TimeSpan.Zero);
        Assert.IsFalse(session.IsEventRunning);
        var zero = Session(Command(EventOperation.ScriptedWalk,0,0,0,0));
        Assert.IsTrue(zero.IsEventRunning);
        Assert.IsNull(zero.ScriptedMotion);
        Assert.AreEqual(new GridPosition(19,20),zero.Player.Position);
        zero.Events!.Advance(TimeSpan.Zero);
        Assert.IsFalse(zero.IsEventRunning);
    }

    [TestMethod]
    public void PairedAnimationWritesBothThreePictureFieldsAndWaitsForTheFinalFrame()
    {
        var session = Session(Command(EventOperation.AnimatePair,0,10,14,1,20));
        for (var frame = 0; frame < 3; frame++)
        {
            foreach (var field in new[] {5,6,7})
            {
                Assert.AreEqual(10+frame*2,session.Scene.Events[0][field]);
                Assert.AreEqual(20+frame*2,session.Scene.Events[1][field]);
            }
            Assert.IsTrue(session.IsEventRunning);
            session.Events!.Advance(EventRunner.AnimationFrameTime);
        }
        Assert.IsFalse(session.IsEventRunning);
    }

    [TestMethod]
    public void PairedAnimationSecondWriteWinsAndPictureWordsWrapWithoutChangingOldAnimate()
    {
        var session = Session(Command(EventOperation.AnimatePair,-1,0,2,-1,32766));
        Assert.AreEqual(32766,session.Player.PictureOverride);
        session.Events!.Advance(EventRunner.AnimationFrameTime);
        Assert.AreEqual(-32768,session.Player.PictureOverride);
        session.Events.Advance(EventRunner.AnimationFrameTime);
        Assert.IsFalse(session.IsEventRunning);
        Assert.ThrowsExactly<ArgumentException>(() => new EventCommand(0,EventOperation.Animate,[-1,32766,32767]));
        var sameEvent = Session(Command(EventOperation.AnimatePair,0,10,13,0,100));
        Assert.AreEqual(100,sameEvent.Scene.Events[0].Picture);
        sameEvent.Events!.Advance(EventRunner.AnimationFrameTime);
        Assert.AreEqual(102,sameEvent.Scene.Events[0].Picture);
        sameEvent.Events.Advance(EventRunner.AnimationFrameTime);
        Assert.IsFalse(sameEvent.IsEventRunning);
    }

    [TestMethod]
    public void EmptyPairedRangeProducesNoWriteAndModernBudgetsAreValidated()
    {
        var session = Session(Command(EventOperation.AnimatePair,-1,14,10,-1,200));
        Assert.IsFalse(session.IsEventRunning);
        Assert.IsNull(session.Player.PictureOverride);
        Assert.ThrowsExactly<ArgumentException>(() => Command(EventOperation.ScriptedWalk,0,0,4097,0));
        Assert.ThrowsExactly<ArgumentException>(() => Command(EventOperation.PanCamera,-32768,0,32767,0));
        Assert.ThrowsExactly<ArgumentException>(() => Command(EventOperation.AnimatePair,200,10,14,1,20));
        Assert.ThrowsExactly<ArgumentException>(() => Command(EventOperation.AnimatePair,0,0,10000,1,20));
    }

    private static EventCommand Command(EventOperation operation,params int[] arguments) => new(0,operation,arguments);
    private static EventProgram Program(EventCommand command) => new(1,[command,new(1,EventOperation.End,[])]);
    private static StoryCatalog Catalog(EventProgram[] programs) => new(70,1,0,null,Direction.Right,programs,[],
        new Dictionary<int,string>(),new Dictionary<int,int>());
    private static GameSession Session(EventCommand command) => new(MovementTests.Scene(),Catalog([Program(command)]));
}
