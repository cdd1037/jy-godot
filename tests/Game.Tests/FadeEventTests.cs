using System.Buffers.Binary;
using JinYong.Content.Legacy;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class FadeEventTests
{
    [TestMethod]
    public void LegacyFadesKeepWordOffsetsAndMapToApproximate450Milliseconds()
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, 13);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), 14);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), -1);
        var program = LegacyEventCompiler.Compile(4, bytes);
        CollectionAssert.AreEqual(new[] { 0,1,2 }, program.Commands.Select(command => command.Id).ToArray());
        Assert.AreEqual(EventOperation.Fade, program.Commands[0].Operation);
        Assert.AreEqual(EventOperation.Fade, program.Commands[1].Operation);
        CollectionAssert.AreEqual(new[] { 0,450 }, program.Commands[0].Arguments);
        CollectionAssert.AreEqual(new[] { 1,450 }, program.Commands[1].Arguments);
        Assert.AreEqual(13, program.Commands[0].SourceOpcode);
        Assert.AreEqual(14, program.Commands[1].SourceOpcode);
    }

    [TestMethod]
    public void FadeValidatesTargetDurationAndArgumentCount()
    {
        foreach (var args in new[] { new[] {-1,450}, new[] {2,450}, new[] {0,-1}, new[] {1,60001}, new[] {1} })
            Assert.ThrowsExactly<ArgumentException>(() => new EventCommand(0, EventOperation.Fade, args));
        _ = new EventCommand(0, EventOperation.Fade, [0,0]);
        _ = new EventCommand(0, EventOperation.Fade, [1,60000]);
    }

    [TestMethod]
    public void FadeUsesActualElapsedAndDoesNotRunFollowingCommandBeforeBoundary()
    {
        var session = new GameSession(MovementTests.Scene(), Catalog(Program(1,
            new(0, EventOperation.Fade, [1,450]), new(1, EventOperation.Face, [(int)Direction.Up]), new(2, EventOperation.End, []))));
        var events = session.Events!;
        Assert.AreEqual(0d, session.FadeOpacity);
        Assert.AreEqual(Direction.Right, session.Player.Facing);
        Assert.IsTrue(events.IsRunning);
        Assert.IsNull(events.Pending);
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Down).Block);
        events.Advance(TimeSpan.FromMilliseconds(225));
        Assert.AreEqual(0.5, session.FadeOpacity, 1e-12);
        events.Advance(TimeSpan.FromMilliseconds(225) - TimeSpan.FromTicks(1));
        Assert.IsTrue(events.IsRunning);
        Assert.AreEqual(Direction.Right, session.Player.Facing);
        events.Advance(TimeSpan.FromTicks(1));
        Assert.AreEqual(1d, session.FadeOpacity);
        Assert.AreEqual(Direction.Up, session.Player.Facing);
        Assert.IsFalse(events.IsRunning);
        Assert.IsTrue(session.TryMove(Direction.Down).Moved);
    }

    [TestMethod]
    public void ConsecutiveFadesWaitIndependentlyAndDoNotStealElapsedFromFutureFrames()
    {
        var world = new RecordingWorld();
        var runner = new EventRunner(Catalog(Program(1, new(0, EventOperation.Fade, [1,450]),
            new(1, EventOperation.Fade, [0,450]), new(2, EventOperation.End, [])), initial: null), world);
        Assert.IsTrue(runner.TryStart(1, 0, new(19,20)));
        CollectionAssert.AreEqual(new[] { 0d }, world.Fades);
        runner.Advance(TimeSpan.FromHours(1));
        Assert.IsTrue(runner.IsRunning);
        CollectionAssert.AreEqual(new[] { 0d,1d,1d }, world.Fades);
        runner.Advance(TimeSpan.FromMilliseconds(225));
        Assert.AreEqual(0.5, world.Fades[^1], 1e-12);
        runner.Advance(TimeSpan.FromMilliseconds(225));
        Assert.AreEqual(0d, world.Fades[^1]);
        Assert.IsFalse(runner.IsRunning);
        Assert.AreEqual(1, runner.CompletedCount);
    }

    [TestMethod]
    public void FadeResultDependsOnElapsedTimeAcrossFrameRates()
    {
        foreach (var fps in new[] {30,60,144})
        {
            var world = new RecordingWorld();
            var runner = new EventRunner(Catalog(Program(1, new(0, EventOperation.Fade, [0,450]),
                new(1, EventOperation.End, [])), initial: null), world);
            runner.TryStart(1, 0, new(19,20));
            var targetElapsed = TimeSpan.FromMilliseconds(226);
            var elapsed = TimeSpan.Zero;
            var frame = TimeSpan.FromSeconds(1d / fps);
            while (elapsed < targetElapsed)
            {
                var delta = frame < targetElapsed - elapsed ? frame : targetElapsed - elapsed;
                runner.Advance(delta);
                elapsed += delta;
            }
            Assert.AreEqual(1 - 226d / 450, world.Fades[^1], 1e-12);
            Assert.IsTrue(runner.IsRunning);
            runner.Advance(TimeSpan.FromMilliseconds(224));
            Assert.AreEqual(0d, world.Fades[^1]);
            Assert.IsFalse(runner.IsRunning);
        }
    }

    [TestMethod]
    public void ZeroDurationFinishesImmediatelyAndNegativeElapsedDoesNotAdvanceFade()
    {
        var world = new RecordingWorld();
        var runner = new EventRunner(Catalog(Program(1, new(0, EventOperation.Fade, [1,0]),
            new(1, EventOperation.Fade, [0,0]), new(2, EventOperation.End, [])), initial: null), world);
        runner.TryStart(1, 0, new(19,20));
        Assert.IsFalse(runner.IsRunning);
        CollectionAssert.AreEqual(new[] { 0d,1d,1d,0d }, world.Fades);
        var session = new GameSession(MovementTests.Scene(), Catalog(Program(1,
            new(0, EventOperation.Fade, [1,450]), new(1, EventOperation.End, []))));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Events!.Advance(TimeSpan.FromTicks(-1)));
        Assert.AreEqual(0d, session.FadeOpacity);
        Assert.IsTrue(session.IsEventRunning);
        session.Events!.Advance(TimeSpan.Zero);
        Assert.AreEqual(0d, session.FadeOpacity);
        Assert.IsTrue(session.IsEventRunning);
    }

    [TestMethod]
    public void FadeHostErrorClearsTimedStateBeforeStartingAnotherEvent()
    {
        var world = new RecordingWorld();
        var catalog = Catalog([Program(1, new(0, EventOperation.Fade, [1,450]), new(1, EventOperation.End, [])),
            Program(2, new(0, EventOperation.Wait, [10]), new(1, EventOperation.End, []))], initial: null);
        var runner = new EventRunner(catalog, world);
        runner.TryStart(1, 0, new(19,20));
        world.ThrowOnFade = true;
        runner.Advance(TimeSpan.FromMilliseconds(100));
        Assert.IsNotNull(runner.Error);
        Assert.AreEqual(EventPresentationKind.Error, runner.Pending!.Kind);
        StringAssert.Contains(runner.Error, "command 0");
        Assert.IsTrue(runner.Confirm(runner.Pending.Token));
        Assert.IsFalse(runner.IsRunning);
        var count = world.Fades.Count;
        Assert.IsTrue(runner.TryStart(2, 0, new(19,20)));
        Assert.IsNull(runner.Error);
        runner.Advance(TimeSpan.FromMilliseconds(10));
        Assert.IsFalse(runner.IsRunning);
        Assert.AreEqual(count, world.Fades.Count);
        Assert.AreEqual(1, runner.CompletedCount);
    }

    [TestMethod]
    public void TestingStartupDrainsFadesAndMatchesNormalCompletion()
    {
        var catalog = Catalog(Program(1, new(0, EventOperation.Fade, [1,450]),
            new(1, EventOperation.Fade, [0,450]), new(2, EventOperation.Face, [(int)Direction.Up]), new(3, EventOperation.End, [])));
        var played = new GameSession(MovementTests.Scene(), catalog);
        EventTests.Drain(played);
        var skipped = new GameSession(MovementTests.Scene(), catalog, skipOpening: true);
        Assert.IsFalse(skipped.IsEventRunning);
        Assert.AreEqual(0d, skipped.FadeOpacity);
        Assert.AreEqual(played.FadeOpacity, skipped.FadeOpacity);
        Assert.AreEqual(played.Player, skipped.Player);
        Assert.AreEqual(1, skipped.Events!.CompletedCount);
        Assert.IsTrue(skipped.TryMove(Direction.Down).Moved);
    }

    private static EventProgram Program(int id, params EventCommand[] commands) => new(id, commands);
    private static StoryCatalog Catalog(EventProgram program, int? initial = 1) => Catalog([program], initial);
    private static StoryCatalog Catalog(EventProgram[] programs, int? initial = 1) => new(70, initial, 0, null,
        Direction.Right, programs, [], new Dictionary<int,string>(), new Dictionary<int,int>());

    private sealed class RecordingWorld : IEventWorld
    {
        public List<double> Fades { get; } = [];
        public bool ThrowOnFade { get; set; }
        public void SetFade(double opacity)
        {
            if (ThrowOnFade) throw new InvalidOperationException("Fade device failed.");
            Fades.Add(opacity);
        }
        public void ChangeEvent(int context, GridPosition trigger, IReadOnlyList<int> arguments) { }
        public void Face(Direction direction) { }
        public void SetPicture(int eventIndex, int picture) { }
        public void AddItem(int id, int count) { }
        public int ItemCount(int id) => 0;
        public int GetVariable(int id) => 0;
        public void SetVariable(int id, int value) { }
        public int EventField(int context, IReadOnlyList<int> arguments) => 0;
        public void SetTile(IReadOnlyList<int> arguments) { }
        public void Teleport(GridPosition position) { }
        public void ValidateCommand(EventCommand command, int context) { }
    }
}
