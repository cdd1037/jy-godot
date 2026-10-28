using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class WalkingPacingTests
{
    [TestMethod]
    [DataRow(30)] [DataRow(60)] [DataRow(144)]
    public void DefaultHeldWalkMovesImmediatelyThenWaitsHalfASecondBeforeRepeating(int fps)
    {
        var session = new GameSession(MovementTests.Scene());
        var walker = new SceneWalker(session);
        walker.ReadInput([Direction.Right], true);
        var elapsed = 0.0;
        double? secondStepAt = null;
        for (var frame = 1; frame <= fps; frame++)
        {
            walker.Advance(1.0 / fps);
            elapsed += 1.0 / fps;
            if (walker.ReadInput([Direction.Right], true) is not null && secondStepAt is null) secondStepAt = elapsed;
            if (elapsed < 0.5 - 1e-9) Assert.AreEqual(1L, session.Player.Steps);
        }
        Assert.IsNotNull(secondStepAt);
        Assert.IsTrue(secondStepAt.Value >= 0.5 - 1e-9 && secondStepAt.Value <= 0.5 + 1.0 / fps);
    }

    [TestMethod]
    public void TapMovesImmediatelyInterpolatesOneStepAndDoesNotRepeatAfterRelease()
    {
        var session = new GameSession(MovementTests.Scene());
        var walker = new SceneWalker(session);
        Assert.IsTrue(walker.ReadInput([Direction.Right], true)!.Value.Moved);
        Assert.AreEqual(0, walker.Progress);
        walker.Advance(walker.Settings.StepSeconds / 2);
        Assert.AreEqual(0.5, walker.Progress, 1e-9);
        Assert.IsNull(walker.ReadInput([], true));
        walker.Advance(walker.Settings.StepSeconds / 2);
        Assert.IsFalse(walker.IsMoving);
        walker.Advance(1);
        Assert.IsNull(walker.ReadInput([], true));
        Assert.AreEqual(1L, session.Player.Steps);
    }

    [TestMethod]
    [DataRow(30)] [DataRow(60)] [DataRow(144)]
    public void HeldWalkKeepsSpeedAcrossDisplayFrameRates(int fps)
    {
        var session = new GameSession(MovementTests.Scene());
        var walker = new SceneWalker(session);
        var times = new List<double> { 0 };
        walker.ReadInput([Direction.Right], true);
        for (var frame = 1; frame <= fps; frame++)
        {
            walker.Advance(1.0 / fps);
            if (walker.ReadInput([Direction.Right], true) is not null) times.Add((double)frame / fps);
        }
        Assert.AreEqual(10L, session.Player.Steps, $"fps={fps}");
        Assert.IsTrue(times[1] >= walker.Settings.RepeatDelaySeconds - 1e-9);
        Assert.AreEqual(10, times.Count);
        Assert.IsTrue(times.Zip(times.Skip(1)).Skip(1).All(pair => pair.Second - pair.First < 0.09));
    }

    [TestMethod]
    public void TurningKeepsInitialDeadlineAndReleaseFallsBackToHeldDirection()
    {
        var session = new GameSession(MovementTests.Scene());
        var walker = new SceneWalker(session, new WalkingSettings(RepeatDelaySeconds: 0.2));
        walker.ReadInput([Direction.Right], true);
        walker.Advance(walker.Settings.StepSeconds);
        Assert.IsNotNull(walker.ReadInput([Direction.Right, Direction.Up], true));
        Assert.AreEqual(Direction.Up, session.Player.Facing);
        walker.Advance(2 * walker.Settings.StepSeconds);
        Assert.IsNull(walker.ReadInput([Direction.Right, Direction.Up], true));
        walker.Advance(walker.Settings.StepSeconds);
        Assert.IsNotNull(walker.ReadInput([Direction.Right, Direction.Up], true));
        walker.Advance(walker.Settings.StepSeconds);
        Assert.IsNotNull(walker.ReadInput([Direction.Right], true));
        Assert.AreEqual(Direction.Right, session.Player.Facing);
        Assert.IsNull(walker.ReadInput([Direction.Left], true), "No second movement attempt in the same frame.");
    }

    [TestMethod]
    public void BlockedAttemptsUseOneWalkFrameAndCannotRunAtDisplayRate()
    {
        var session = new GameSession(MovementTests.Scene(spawn: new(0, 0)));
        var walker = new SceneWalker(session);
        Assert.AreEqual(MovementBlock.Boundary, walker.ReadInput([Direction.Left], true)!.Value.Block);
        Assert.AreEqual(2, session.Player.WalkFrameOffset);
        for (var i = 0; i < 10; i++)
        {
            walker.Advance(0.01);
            Assert.IsNull(walker.ReadInput([Direction.Left], true));
        }
        walker.Advance(walker.Settings.RepeatDelaySeconds - 0.1);
        Assert.AreEqual(MovementBlock.Boundary, walker.ReadInput([Direction.Left], true)!.Value.Block);
        Assert.AreEqual(4, session.Player.WalkFrameOffset);
        Assert.IsFalse(walker.IsMoving);
        Assert.AreEqual(0L, session.Player.Steps);
    }

    [TestMethod]
    public void WalkFramesCyclePerAttemptAndReturnToStandingAfterAnIdlePause()
    {
        var session = new GameSession(MovementTests.Scene());
        var walker = new SceneWalker(session);
        var expected = new[] { 2, 4, 6, 8, 10, 12, 2 };
        foreach (var offset in expected)
        {
            walker.ReadInput([Direction.Right], true);
            Assert.AreEqual(offset, session.Player.WalkFrameOffset);
            walker.ReadInput([], true);
            walker.Advance(walker.Settings.StepSeconds);
        }
        walker.Advance(walker.Settings.StandingDelaySeconds - 0.001);
        walker.ReadInput([], true);
        Assert.AreEqual(2, session.Player.WalkFrameOffset);
        walker.Advance(0.001);
        walker.ReadInput([], true);
        Assert.AreEqual(0, session.Player.WalkFrameOffset);
    }

    [TestMethod]
    public void UiAndStoryBlockHeldKeysUntilReleaseAndDoNotReplayMissedTicks()
    {
        var session = new GameSession(MovementTests.Scene());
        var walker = new SceneWalker(session);
        walker.ReadInput([Direction.Right], false);
        walker.Advance(1);
        Assert.IsNull(walker.ReadInput([Direction.Right], true));
        walker.ReadInput([], true);
        Assert.IsNotNull(walker.ReadInput([Direction.Right], true));
        walker.Advance(5);
        Assert.IsNotNull(walker.ReadInput([Direction.Right], true));
        Assert.IsNull(walker.ReadInput([Direction.Right], true));
        Assert.AreEqual(2L, session.Player.Steps, "A stalled host must not burst through missed movement ticks.");

        var story = new StoryCatalog(70, 1, 0, 5002, Direction.Right,
            [new EventProgram(1, [new(0, EventOperation.Dialogue, [1, 0, 1]), new(1, EventOperation.End, [])])],
            [new DialogueDefinition(1, ["暂停移动"])], new Dictionary<int, string>(), new Dictionary<int, int>());
        var talking = new GameSession(MovementTests.Scene(), story);
        var talkingWalker = new SceneWalker(talking);
        Assert.IsNull(talkingWalker.ReadInput([Direction.Right], true));
        talking.Events!.Confirm(talking.Events.Pending!.Token);
        Assert.IsNull(talkingWalker.ReadInput([Direction.Right], true));
        talkingWalker.ReadInput([], true);
        Assert.IsNotNull(talkingWalker.ReadInput([Direction.Right], true));
    }
}
