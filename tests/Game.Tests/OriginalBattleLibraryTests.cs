using JinYong.Importing.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class OriginalBattleLibraryTests
{
    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void BattleViewUsesTheGlobalCatalogWithoutExportingOrReplacingExplorationState()
    {
        var library = OriginalLibraryFixture.Library;
        var catalog = library.Battles!;
        Assert.HasCount(140, catalog.Definitions); Assert.HasCount(26, catalog.Fields);
        Assert.HasCount(93, catalog.Magics); Assert.HasCount(200, catalog.Items);
        Assert.AreEqual(52000, catalog.Progression!.ExperienceThresholds[^1]);
        Assert.IsTrue(library.Manifest.SourceHashes.ContainsKey("Z.DAT"));
        Assert.IsFalse(library.Sources.ContainsKey("Z.DAT"));
        var sourceData = LegacyBattleImporter.Read(name => File.ReadAllBytes(library.Resolve(library.Sources[name].Storage)),
            LegacyArchive.Parse(File.ReadAllBytes(library.Resolve(library.Sources["RANGER.IDX"].Storage)),
                File.ReadAllBytes(library.Resolve(library.Sources["RANGER.GRP"].Storage))));
        foreach (var field in sourceData.Fields)
            CollectionAssert.AreEqual(field.Layers.ToArray(), catalog.Fields[field.Id].Layers.ToArray());
        var filesBefore = Directory.GetFiles(library.Directory, "*", SearchOption.AllDirectories).Order().ToArray();
        var overlay = ContentJson.Read<StoryDocument>(Path.Combine(LegacyFixture.Root, "content/examples/battle-demo.json"));
        var scene = library.OpenScene(70, overlay: overlay);
        var session = new GameSession(scene.Scene, scene.Story, skipOpening: true, battles: catalog);
        var sceneState = session.Scene; var events = session.Events; var party = session.Party;
        Assert.IsTrue(session.Interact());
        Assert.IsTrue(session.Events!.Choose(session.Events.Pending!.Token, 0));
        Assert.AreEqual(0, session.Battle!.HpCostScale);
        var view = library.OpenBattleView(session);
        Assert.AreSame(library, view.Library); Assert.AreSame(session.Story, view.Story);
        Assert.AreSame(session.Definition, view.Scene); Assert.AreSame(sceneState, session.Scene);
        Assert.AreSame(events, session.Events); Assert.AreSame(party, session.Party);
        CollectionAssert.AreEquivalent(library.Sprites.Banks["battlefield/000"].Keys.ToArray(), view.Document.Sprites.Select(sprite => sprite.LegacyId).ToArray());
        CollectionAssert.AreEqual(filesBefore, Directory.GetFiles(library.Directory, "*", SearchOption.AllDirectories).Order().ToArray());
    }
}
