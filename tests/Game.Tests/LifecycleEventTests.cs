using System.Buffers.Binary;
using JinYong.Importing.Legacy;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Persistence;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class LifecycleEventTests
{
    [TestMethod]
    public void CompilerPreservesIgnoredDeathWordAndStopsAtOuterLifecycleHandoff()
    {
        // Original sub_2E659 ignores this word; it is not a dialogue ID.
        var death = LegacyEventCompiler.Compile(1, Bytes(15,83,8));
        Assert.HasCount(1,death.Commands);
        Assert.AreEqual(EventOperation.Death,death.Commands[0].Operation);
        CollectionAssert.AreEqual(new[] {83},death.Commands[0].Arguments);
        Assert.AreEqual(15,death.Commands[0].SourceOpcode);
        var load = LegacyEventCompiler.Compile(2, Bytes(24,8));
        Assert.HasCount(1,load.Commands);
        Assert.AreEqual(EventOperation.LoadMenu,load.Commands[0].Operation);
        Assert.IsEmpty(load.Commands[0].Arguments);
        Assert.AreEqual(24,load.Commands[0].SourceOpcode);
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyEventCompiler.Compile(1,Bytes(15)));
        Assert.ThrowsExactly<ArgumentException>(() => new EventCommand(0,EventOperation.Death,[32768]));
        Assert.ThrowsExactly<ArgumentException>(() => new EventCommand(0,EventOperation.LoadMenu,[0]));
    }

    [TestMethod]
    public void DeathNeverShowsItsIgnoredArgumentAsDialogueOrResumesFollowingStory()
    {
        var session = Session(new EventProgram(1,[new(0,EventOperation.Death,[83]),
            new(1,EventOperation.Face,[(int)Direction.Up]),new(2,EventOperation.End,[])]));
        var runner = session.Events!; var request = session.PendingLifecycle!;
        Assert.AreEqual(EventLifecycleKind.Death,request.Kind); Assert.AreEqual(83,request.SourceArgument);
        Assert.IsNull(runner.Pending); Assert.IsTrue(runner.IsRunning);
        Assert.AreEqual(MovementBlock.Story,session.TryMove(Direction.Down).Block);
        Assert.IsFalse(session.Interact()); Assert.IsFalse(session.UseItem(174));
        Assert.IsFalse(runner.Confirm(request.Token)); Assert.IsFalse(runner.Choose(request.Token,0));
        Assert.IsFalse(runner.CancelChoice(request.Token)); Assert.IsFalse(runner.TryStart(1,0,new(19,20)));
        session.Advance(TimeSpan.FromDays(1));
        Assert.AreSame(request,session.PendingLifecycle); Assert.AreEqual(Direction.Right,session.Player.Facing);
        Assert.AreEqual(0,runner.CompletedCount); Assert.IsFalse(runner.CanCaptureIdleState);
        Assert.ThrowsExactly<InvalidOperationException>(() => runner.CaptureIdleState());
    }

    [TestMethod]
    public void LoadMenuIsDistinctFromDeathAndFailedLoadKeepsFrozenRequest()
    {
        var session = Session(new EventProgram(1,[new(0,EventOperation.LoadMenu,[])]));
        var request = session.PendingLifecycle!;
        Assert.AreEqual(EventLifecycleKind.LoadMenu,request.Kind); Assert.AreEqual(-1,request.SourceArgument);
        // A candidate failure does not acknowledge or complete the old VM.
        Assert.ThrowsExactly<ArgumentException>(() => new GameSession(new SceneDefinition("bad","bad",new(0,0),[],[])));
        session.Advance(TimeSpan.FromSeconds(1));
        Assert.AreSame(request,session.PendingLifecycle); Assert.IsFalse(session.IsRetired);
        Assert.AreEqual(MovementBlock.Story,session.TryMove(Direction.Down).Block);
    }

    [TestMethod]
    public void LifecycleWaitsForPriorFadeAndRetiringDiscardsNestedCallStackPermanently()
    {
        var entry = new EventProgram(1,[new(0,EventOperation.Call,[2]),new(1,EventOperation.Face,[(int)Direction.Up]),new(2,EventOperation.End,[])]);
        var callee = new EventProgram(2,[new(0,EventOperation.Fade,[1,450]),new(1,EventOperation.Death,[-1]),new(2,EventOperation.Return,[])]);
        var session = new GameSession(MovementTests.Scene(),Catalog([entry,callee]));
        Assert.IsNull(session.PendingLifecycle); session.Advance(TimeSpan.FromMilliseconds(449));
        Assert.IsNull(session.PendingLifecycle); session.Advance(TimeSpan.FromMilliseconds(1));
        Assert.AreEqual(1d,session.FadeOpacity); Assert.AreEqual(EventLifecycleKind.Death,session.PendingLifecycle!.Kind);
        session.RetireForReplacement();
        Assert.IsTrue(session.IsRetired); Assert.IsTrue(session.Events!.IsRetired);
        Assert.IsNull(session.PendingLifecycle); Assert.IsFalse(session.Events.IsRunning);
        Assert.IsFalse(session.Events.TryStart(1,0,new(19,20))); Assert.IsFalse(session.Events.CanCaptureIdleState);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Events.RestoreIdleState(new(1,0,[])));
        session.Events.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(Direction.Right,session.Player.Facing); Assert.AreEqual(0,session.Events.CompletedCount);
    }

    [TestMethod]
    public void TestingOpeningCannotAutomaticallyAnswerDeathOrLoadMenus()
    {
        foreach (var command in new[] {new EventCommand(0,EventOperation.Death,[83]),new EventCommand(0,EventOperation.LoadMenu,[])})
            Assert.ThrowsExactly<InvalidOperationException>(() => new GameSession(MovementTests.Scene(),
                Catalog([new EventProgram(1,[command])]),skipOpening:true));
    }

    [TestMethod]
    public void IdleSnapshotRestoresRandomSequenceTokensAndDuplicateCompletionsOnly()
    {
        var program = new EventProgram(1,[new(0,EventOperation.RandomDialogue,[10,3,0,1]),new(1,EventOperation.End,[])]);
        var session = Session(program); var runner = session.Events!;
        Assert.IsTrue(runner.Confirm(runner.Pending!.Token));
        Assert.IsTrue(runner.TryStart(1,0,new(19,20))); Assert.IsTrue(runner.Confirm(runner.Pending!.Token));
        var snapshot = runner.CaptureIdleState();
        CollectionAssert.AreEqual(new[] {1,1},snapshot.CompletedPrograms.ToArray());
        var restored = new GameSession(MovementTests.Scene(),Catalog([program],initial:null)).Events!;
        restored.RestoreIdleState(snapshot);
        Assert.IsEmpty(restored.Trace); Assert.IsEmpty(restored.TakeSounds()); Assert.IsNull(restored.Error);
        Assert.IsTrue(runner.TryStart(1,0,new(19,20))); Assert.IsTrue(restored.TryStart(1,0,new(19,20)));
        Assert.AreEqual(runner.Pending,restored.Pending);
        Assert.IsFalse(restored.Confirm(snapshot.Token));
        Assert.AreEqual(2,snapshot.CompletedPrograms.Count);
    }

    [TestMethod]
    public void InvalidIdleSnapshotDoesNotPartiallyOverwriteRunnerState()
    {
        var program = new EventProgram(1,[new(0,EventOperation.End,[])]);
        var runner = Session(program).Events!; var before = runner.CaptureIdleState();
        foreach (var invalid in new[] {new EventRunnerIdleSnapshot(99,-1,[]),new(99,long.MaxValue,[]),
            new(99,4,[-1]),new(99,4,[999])})
        {
            Assert.ThrowsExactly<ArgumentException>(() => runner.RestoreIdleState(invalid));
            var after = runner.CaptureIdleState();
            Assert.AreEqual(before.RandomState,after.RandomState); Assert.AreEqual(before.Token,after.Token);
            CollectionAssert.AreEqual(before.CompletedPrograms.ToArray(),after.CompletedPrograms.ToArray());
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => runner.RestoreIdleState(new(1,0,null!)));
    }

    [TestMethod]
    public void IdleSnapshotNeverReplaysPastSoundsOrTraceAndRejectsActiveContinuations()
    {
        var done = new EventProgram(1,[new(0,EventOperation.Sound,[24]),new(1,EventOperation.End,[])]);
        var source = Session(done).Events!; var snapshot = source.CaptureIdleState();
        var target = new GameSession(MovementTests.Scene(),Catalog([done],initial:null)).Events!;
        target.RestoreIdleState(snapshot);
        CollectionAssert.AreEqual(new[] {24},source.TakeSounds());
        Assert.IsEmpty(target.TakeSounds()); Assert.IsEmpty(target.Trace);
        foreach (var command in new[] {new EventCommand(0,EventOperation.Wait,[20]),new EventCommand(0,EventOperation.Refresh,[]),
            new EventCommand(0,EventOperation.Fade,[1,450]),new EventCommand(0,EventOperation.Dialogue,[10,0,1])})
        {
            var active = Session(new EventProgram(1,[command,new(1,EventOperation.End,[])])).Events!;
            Assert.IsFalse(active.CanCaptureIdleState);
            Assert.ThrowsExactly<InvalidOperationException>(() => active.RestoreIdleState(snapshot));
        }
    }

    private static GameSession Session(EventProgram program) => new(MovementTests.Scene(),Catalog([program]));
    private static StoryCatalog Catalog(EventProgram[] programs,int? initial = 1) => new(70,initial,0,null,Direction.Right,
        programs,[new(10,["one"]),new(11,["two"]),new(12,["three"]),new(83,["must not appear"])],
        new Dictionary<int,string>(),new Dictionary<int,int>());
    private static byte[] Bytes(params short[] words)
    {
        var bytes = new byte[words.Length*2];
        for(var index=0;index<words.Length;index++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index*2),words[index]);
        return bytes;
    }
}
