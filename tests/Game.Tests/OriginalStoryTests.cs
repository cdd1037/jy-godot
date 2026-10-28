using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class OriginalStoryTests
{
    private static readonly Lazy<ScenePackage> Original = new(() => OriginalLibraryFixture.Library.OpenScene(70));

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualOpeningHasAllDialogueAnimationSoundAndDisablesItsTrigger()
    {
        var package = Original.Value;
        var library = OriginalLibraryFixture.Library;
        Assert.AreEqual(3, package.Manifest.FormatVersion);
        Assert.AreSame(library, package.Library);
        CollectionAssert.AreEquivalent(library.Manifest.SourceHashes.Keys.ToArray(), package.Manifest.SourceHashes.Keys.ToArray());
        CollectionAssert.AreEquivalent(library.Manifest.FileHashes.Keys.ToArray(), package.Manifest.FileHashes.Keys.ToArray());
        Assert.AreEqual(691, package.Story!.InitialProgramId);
        Assert.AreEqual(66, package.Story.Programs[691].Commands.Count);
        Assert.AreEqual(882, package.Story.Programs.Count);
        Assert.IsTrue(package.Story.Dialogues.Count > 47);
        Assert.IsTrue(package.Document.Sprites.Length > 3000);
        Assert.AreEqual(320, package.Story.Party!.Roles.Count);
        Assert.AreEqual(100, package.Story.World!.Scenes.Count);
        Assert.AreEqual(84, package.Story.World.Scenes.Count(scene => scene.HasLegacyMetadata));
        Assert.IsTrue(package.Document.Sounds.ContainsKey(24));
        CollectionAssert.IsSubsetOf(new[] { 0,114 }, package.Document.Portraits.Select(portrait => portrait.LegacyId).ToArray());
        var session = new GameSession(package.Scene, package.Story);
        Assert.AreEqual(6890, session.Player.PictureOverride);
        Assert.AreEqual(2520, session.Events!.Pending!.DialogueId);
        StringAssert.StartsWith(session.Events.Pending.Text, "啊！頭好痛呀！");
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Up).Block);
        var seen = EventTests.Drain(session);
        CollectionAssert.AreEquivalent(Enumerable.Range(2520, 27).ToArray(), seen.Distinct().ToArray());
        Assert.AreEqual((short)-1, session.Scene.Events[0][4]);
        Assert.AreEqual((short)692, session.Scene.Events[1][2]);
        Assert.AreEqual(6888, session.Scene.Events[1].Picture);
        Assert.AreEqual(Direction.Up, session.Player.Facing);
        Assert.IsNull(session.Player.PictureOverride);
        CollectionAssert.AreEqual(new[] { 24 }, session.Events.TakeSounds());
        Assert.AreEqual(4, session.Inventory.Count);
        Assert.AreEqual(3, session.Inventory[0]);
        Assert.AreEqual((short)691, package.Scene.Events[0][4]); // Static definition remains unchanged.
        session.CompleteMovement();
        Assert.AreEqual(1, session.Events.CompletedCount);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void TestingStartupMatchesCompletedOpeningWithoutPromptsOrSoundAndAllowsMovement()
    {
        var package = Original.Value;
        var played = new GameSession(package.Scene, package.Story);
        EventTests.Drain(played);
        var skipped = new GameSession(package.Scene, package.Story, skipOpening: true);
        Assert.IsFalse(skipped.IsEventRunning);
        Assert.IsNull(skipped.Events!.Pending);
        Assert.IsNull(skipped.Events.Error);
        Assert.AreEqual(0, skipped.Events.TakeSounds().Length);
        Assert.AreEqual(played.Player, skipped.Player);
        CollectionAssert.AreEqual(played.Inventory.OrderBy(pair => pair.Key).ToArray(),
            skipped.Inventory.OrderBy(pair => pair.Key).ToArray());
        for (var index = 0; index < 200; index++)
        for (var word = 0; word < 11; word++)
            Assert.AreEqual(played.Scene.Events[index][word], skipped.Scene.Events[index][word], $"event={index}, word={word}");
        Assert.AreEqual((short)691, package.Scene.Events[0][4]);
        Assert.IsTrue(skipped.TryMove(Direction.Up).Moved);
        skipped.CompleteMovement();
        Assert.AreEqual(1, skipped.Events.CompletedPrograms.Count(id => id == 691));
        FaceEvent(skipped, 1);
        Assert.IsTrue(skipped.Interact());
        Assert.AreEqual(2555, skipped.Events.Pending!.DialogueId);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void AuthoredEventExampleSupportsDecliningDeliveryAndExactlyOneReward()
    {
        var original = Original.Value;
        var library = OriginalLibraryFixture.Library;
        var files = Directory.GetFiles(library.Directory, "*", SearchOption.AllDirectories);
        var overlay = ContentJson.Read<StoryDocument>(Path.Combine(LegacyFixture.Root, "content", "examples", "event-demo.json"));
        var example = library.OpenScene(70, overlay: overlay);
        Assert.AreSame(library, example.Library);
        Assert.AreEqual(original.Directory, example.Directory);
        CollectionAssert.AreEquivalent(files, Directory.GetFiles(library.Directory, "*", SearchOption.AllDirectories));
        var session = new GameSession(example.Scene, example.Story, skipOpening: true);
        Assert.IsTrue(session.Interact());
        Assert.AreEqual(EventPresentationKind.Choice, session.Events!.Pending!.Kind);
        session.Events.CancelChoice(session.Events.Pending.Token);
        EventTests.Drain(session);
        Assert.AreEqual(0, session.Variables.Count);
        Assert.IsTrue(session.Interact());
        session.Events.Choose(session.Events.Pending!.Token, 0);
        EventTests.Drain(session);
        Assert.AreEqual(1, session.Inventory[9000]);
        Assert.AreEqual(1, session.Variables["example:quest/progress"]);
        Assert.IsTrue(session.Interact());
        EventTests.Drain(session);
        Assert.AreEqual(1, session.Inventory[9000]);
        Assert.IsTrue(session.UseItem(0));
        Assert.AreEqual(10005, session.Events.Pending!.DialogueId);
        EventTests.Drain(session);
        var silver = session.Inventory.GetValueOrDefault(174);
        Assert.IsTrue(session.UseItem(9000));
        Assert.IsTrue(session.IsEventRunning);
        Assert.IsNull(session.Events.Pending); // Script is waiting after consuming the token.
        EventTests.Drain(session);
        Assert.AreEqual(2, session.Variables["example:quest/progress"]);
        Assert.AreEqual(0, session.Inventory.GetValueOrDefault(9000));
        Assert.AreEqual(silver + 100, session.Inventory[174]);
        Assert.IsTrue(session.Interact());
        Assert.AreEqual(10002, session.Events.Pending!.DialogueId);
        EventTests.Drain(session);
        Assert.AreEqual(silver + 100, session.Inventory[174]);
        Assert.IsFalse(session.UseItem(9000));
        Assert.AreEqual(691, original.Story!.InitialProgramId);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualDollAndAllEightChestsWorkAfterOpeningWithoutDuplicateRewards()
    {
        var package = Original.Value;
        var session = new GameSession(package.Scene, package.Story);
        EventTests.Drain(session);
        FaceEvent(session, 1);
        Assert.IsTrue(session.Interact());
        Assert.AreEqual(2555, session.Events!.Pending!.DialogueId);
        EventTests.Drain(session);
        (int Event, int Item, int Count)[] rewards =
        [(2,1,3),(3,11,3),(4,3,3),(5,22,3),(6,174,100),(7,174,100),(8,186,5),(9,171,10)];
        foreach (var reward in rewards)
        {
            FaceEvent(session, reward.Event);
            var before = session.Inventory.GetValueOrDefault(reward.Item);
            Assert.IsTrue(session.Interact(), $"Chest {reward.Event} did not start.");
            session.Events.Advance(TimeSpan.Zero); // Original opcode 0 presents the opened chest before its reward.
            Assert.IsNotNull(session.Events.Pending);
            Assert.AreEqual(EventPresentationKind.Item, session.Events.Pending!.Kind);
            Assert.AreEqual(before + reward.Count, session.Inventory[reward.Item]);
            Assert.AreEqual((short)-1, session.Scene.Events[reward.Event][2]);
            Assert.AreNotEqual(package.Scene.Events[reward.Event].Picture, session.Scene.Events[reward.Event].Picture);
            EventTests.Drain(session);
            Assert.IsFalse(session.Interact());
            Assert.AreEqual(before + reward.Count, session.Inventory[reward.Item]);
        }
        Assert.AreEqual(200, session.Inventory[174]);
        Assert.AreEqual(1, session.Events.CompletedPrograms.Count(id => id == 691));
        FaceEvent(session, 10);
        Assert.IsTrue(session.Interact());
        Assert.AreEqual(EventPresentationKind.Choice, session.Events.Pending!.Kind);
        Assert.IsTrue(session.Events.Choose(session.Events.Pending.Token, 1));
        Assert.IsFalse(session.IsEventRunning);
        Assert.IsNull(session.InteractionMessage);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualBedRestRunsOriginalChoiceDialoguesFadesAndPartyRecovery()
    {
        var package = Original.Value;
        Assert.IsTrue(package.Story!.Programs.ContainsKey(931));
        var session = new GameSession(package.Scene, package.Story, skipOpening: true);
        session.Party!.SetWord(0, RoleWords.Hp, 1);
        session.Party.SetWord(0, RoleWords.Mp, 0);
        session.Party.SetWord(0, RoleWords.Hurt, 10);
        session.Party.SetWord(0, RoleWords.Poison, 0);
        FaceEvent(session, 10);
        Assert.IsTrue(session.Interact());
        Assert.AreEqual("是否休息？", session.Events!.Pending!.Text);
        Assert.IsTrue(session.Events.Choose(session.Events.Pending.Token, 0));
        Assert.AreEqual(2843, session.Events.Pending!.DialogueId);
        session.Events.Confirm(session.Events.Pending.Token);
        session.Events.Advance(TimeSpan.Zero); // Present refresh, then start fading to black.
        Assert.AreEqual(0d, session.FadeOpacity);
        session.Events.Advance(TimeSpan.FromMilliseconds(225));
        Assert.AreEqual(0.5d, session.FadeOpacity, 0.0001);
        Assert.AreEqual(1, session.Party.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(MovementBlock.Story, session.TryMove(Direction.Down).Block);
        session.Events.Advance(TimeSpan.FromMilliseconds(225));
        Assert.AreEqual(1d, session.FadeOpacity);
        Assert.AreEqual(session.Party.GetWord(0, RoleWords.MaximumHp), session.Party.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(session.Party.GetWord(0, RoleWords.MaximumMp), session.Party.GetWord(0, RoleWords.Mp));
        Assert.AreEqual(0, session.Party.GetWord(0, RoleWords.Hurt));
        Assert.AreEqual(Direction.Down, session.Player.Facing);
        session.Events.Advance(TimeSpan.Zero); // Refresh, then fade back in.
        session.Events.Advance(TimeSpan.FromMilliseconds(450));
        Assert.AreEqual(0d, session.FadeOpacity);
        Assert.AreEqual(2844, session.Events.Pending!.DialogueId);
        EventTests.Drain(session);
        Assert.IsFalse(session.IsEventRunning);
        Assert.IsNull(session.Events.Error);
        Assert.AreEqual(1, session.Events.CompletedPrograms.Count(id => id == 931));
        Assert.AreNotEqual(1, package.Story.Party!.Roles[0].Words[RoleWords.Hp]);
        Assert.AreNotEqual(MovementBlock.Story, session.TryMove(Direction.Up).Block);
    }

    private static void FaceEvent(GameSession session, int index)
    {
        var targetEvent = session.Scene.Events[index];
        var target = new GridPosition(targetEvent[9], targetEvent[10]);
        var queue = new Queue<GridPosition>();
        var paths = new Dictionary<GridPosition, List<Direction>> { [session.Player.Position] = [] };
        queue.Enqueue(session.Player.Position);
        while (queue.TryDequeue(out var point))
        {
            foreach (var direction in Enum.GetValues<Direction>())
            {
                if (point.Step(direction) == target)
                {
                    foreach (var step in paths[point])
                    {
                        Assert.IsTrue(session.TryMove(step).Moved);
                        session.CompleteMovement();
                    }
                    var face = session.TryMove(direction);
                    Assert.IsFalse(face.Moved, "Interaction target should block movement.");
                    return;
                }
                var next = point.Step(direction);
                if (paths.ContainsKey(next) || SceneMovementRules.Check(session.Scene, point, next) != MovementBlock.None) continue;
                paths[next] = [.. paths[point], direction]; queue.Enqueue(next);
            }
        }
        Assert.Fail($"No path to event {index}.");
    }
}
