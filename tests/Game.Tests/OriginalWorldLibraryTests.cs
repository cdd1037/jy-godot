using JinYong.Importing.Legacy;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class OriginalWorldLibraryTests
{
    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void MainMapAndTravelStartRoundTripThroughTheIndependentLibrary()
    {
        var library = OriginalLibraryFixture.Library;
        byte[] Read(string name) => File.ReadAllBytes(library.Resolve(library.Sources[name].Storage));
        var expected = LegacyWorldMapImporter.Read(Read);
        Assert.IsNotNull(library.MainMap);
        foreach (var layer in Enum.GetValues<WorldLayer>())
            CollectionAssert.AreEqual(expected.Layer(layer).ToArray(), library.MainMap.Layer(layer).ToArray());
        var header = LegacyArchive.Parse(Read("RANGER.IDX"), Read("RANGER.GRP")).Entry(0);
        Assert.AreEqual(LegacyWorldMapImporter.ReadState(header), library.GameData.TravelStart);
        var scene = library.OpenScene(70);
        var session = new GameSession(scene.Scene, scene.Story, skipOpening: true,
            worldMap: library.MainMap, travelStart: library.GameData.TravelStart);
        var view = library.OpenWorldView(session);
        Assert.AreSame(library, view.Library);
        Assert.AreSame(session.Story, view.Story);
        Assert.AreSame(session.Definition, view.Scene);
        Assert.AreEqual(-1, view.Document.LegacySceneId);
        CollectionAssert.AreEquivalent(library.Sprites.Banks["world"].Keys.ToArray(),
            view.Document.Sprites.Select(sprite => sprite.LegacyId).ToArray());
        Assert.AreEqual(library.Directory, view.Directory);
    }
}
