using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class IncrementEventImportTests
{
    private static readonly Lazy<ScenePackage> Keys = new(() => OriginalLibraryFixture.OpenFreeScene(79));
    private static readonly Lazy<ScenePackage> Npcs = new(() => OriginalLibraryFixture.OpenFreeScene(74));

    private static void FaceEvent(GameSession session, int index)
    {
        var point = session.Scene.Events[index];
        var world = (IEventWorld)session;
        world.Teleport(new(point[9], point[10] + 1)); world.Face(Direction.Up);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    [DataRow(0)] [DataRow(1)]
    public void ActualCopperKeysImportBothDynamicOutcomesAndOpenTheDoorInEitherOrder(int first)
    {
        var package = Keys.Value;
        Assert.IsTrue(package.Story!.Programs.ContainsKey(648)); Assert.IsTrue(package.Story.Programs.ContainsKey(650));
        var session = new GameSession(package.Scene, package.Story);
        ((IEventWorld)session).AddItem(187, 1); ((IEventWorld)session).AddItem(188, 1);
        FaceEvent(session, first);
        Assert.IsTrue(session.UseItem(187 + first), session.InteractionMessage);
        EventTests.Drain(session);
        Assert.IsFalse(session.Inventory.ContainsKey(187 + first));
        var second = 1 - first;
        Assert.AreEqual(second == 0 ? 648 : 650, session.Scene.Events[second][3]);
        FaceEvent(session, second);
        Assert.IsTrue(session.UseItem(187 + second), session.InteractionMessage);
        EventTests.Drain(session);
        Assert.IsNull(session.Events!.Error);
        Assert.IsFalse(session.Inventory.ContainsKey(187 + second));
        Assert.AreEqual(-1, session.Scene.Events[0][3]); Assert.AreEqual(-1, session.Scene.Events[1][3]);
        Assert.AreEqual((short)0, session.Scene.At(MapLayer.Building, new(23, 39)));
        Assert.AreEqual((short)0, session.Scene.At(MapLayer.Building, new(24, 39)));
        Assert.AreEqual(2, session.Events.CompletedCount);
        Assert.AreEqual((short)647, package.Scene.Events[0][3]); Assert.AreEqual((short)649, package.Scene.Events[1][3]);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualFiveNpcUpdatesImportEveryCounterOutcomeAndUnlockTheFinalEvent()
    {
        var package = Npcs.Value;
        foreach (var id in Enumerable.Range(354, 6)) Assert.IsTrue(package.Story!.Programs.ContainsKey(id), $"Missing counter program {id}.");
        var session = new GameSession(package.Scene, package.Story);
        Assert.IsTrue(session.Party!.Join(38).Joined); // Original NPC branches require Murong Fu in the party.
        var updates = new[] { (353, 2), (360, 8), (361, 9), (362, 10), (383, 7) };
        for (var step = 0; step < updates.Length; step++)
        {
            var (script, point) = updates[step];
            var target = session.Scene.Events[point];
            Assert.IsTrue(session.Events!.TryStart(script, point, new(target[9], target[10])), session.Events.Error);
            EventTests.Drain(session);
            foreach (var counter in new[] { 4, 5, 6 }) Assert.AreEqual(355 + step, session.Scene.Events[counter][4]);
            // Exercise the currently computed script, including the otherwise
            // unreferenced empty programs 355–358, after every NPC update.
            var tick = session.Scene.Events[4];
            Assert.IsTrue(session.Events.TryStart(tick[4], 4, new(tick[9], tick[10])), session.Events.Error);
            EventTests.Drain(session);
        }
        foreach (var counter in new[] { 4, 5, 6 }) Assert.AreEqual((short)-1, session.Scene.Events[counter][4]);
        Assert.AreEqual((short)363, session.Scene.Events[12][2]); Assert.AreEqual((short)363, session.Scene.Events[13][2]);
        Assert.IsNull(session.Events!.Error); Assert.AreEqual(10, session.Events.CompletedCount);
        foreach (var counter in new[] { 4, 5, 6 }) Assert.AreEqual((short)354, package.Scene.Events[counter][4]);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void GlobalLibraryLoadsComputedScriptChainsWithoutSceneDependencyProfiles()
    {
        var library = OriginalLibraryFixture.Library;
        var home = library.OpenScene(70);
        var remote = OriginalLibraryFixture.OpenFreeScene(79);
        Assert.AreSame(library, home.Library); Assert.AreSame(library, remote.Library);
        Assert.AreEqual(home.Directory, remote.Directory);
        // This verifies global availability and reference completeness only;
        // it does not claim end-to-end completion of these larger storylines.
        foreach (var source in new[] { 701, 751 })
        {
            Assert.IsTrue(home.Story!.Programs[source].Commands.Any(command => command.Operation == EventOperation.IncrementEventScripts));
            Assert.IsTrue(remote.Story!.Programs.ContainsKey(source + 1));
        }
        foreach (var id in Enumerable.Range(701, 50).Concat(Enumerable.Range(751, 50)))
            Assert.IsTrue(home.Story!.Programs.ContainsKey(id), $"Global script {id} is missing.");
    }
}
