using System.Text.Json;
using JinYong.Content.Packages;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class BattleAnimationViewTests
{
    [TestMethod]
    public void AnimationBanksAreAnInMemoryViewAndDoNotChangePackageJson()
    {
        var document = new SceneDocument { BattleSpriteBanks = new Dictionary<string,SpriteAsset[]>
        {
            ["fight/000"] = [new() { LegacyId=16000,Atlas="assets/sprites/a.png",Width=1,Height=1 }],
            ["fight/001"] = [new() { LegacyId=16000,Atlas="assets/sprites/b.png",Width=1,Height=1 }]
        } };
        var json = JsonSerializer.Serialize(document,ContentJson.Options);
        Assert.IsFalse(json.Contains("battle_sprite_banks",StringComparison.Ordinal));
        Assert.AreNotEqual(document.BattleSpriteBanks["fight/000"][0].Atlas,document.BattleSpriteBanks["fight/001"][0].Atlas);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void BattleZeroPreloadsEnemyAndUnconfirmedSelectableHeadsAndKeepsLocalIdsSeparate()
    {
        var library = OriginalLibraryFixture.Library;
        var overlay = ContentJson.Read<StoryDocument>(Path.Combine(LegacyFixture.Root,"content/examples/battle-demo.json"));
        var scene = library.OpenScene(70,overlay:overlay);
        var session = new GameSession(scene.Scene,scene.Story,skipOpening:true,battles:library.Battles);
        Assert.IsTrue(session.Party!.Join(2).Joined); // Optional ally, not yet confirmed for battle0.
        Assert.IsTrue(session.Interact()); Assert.IsTrue(session.Events!.Choose(session.Events.Pending!.Token,0));
        var battle = session.Battle!;
        Assert.IsFalse(battle.Combatants.Any(actor=>!actor.IsFriendly));
        var filesBefore=Directory.GetFiles(library.Directory,"*",SearchOption.AllDirectories).Order().ToArray();
        var view=library.OpenBattleView(session); var banks=view.Document.BattleSpriteBanks;
        CollectionAssert.AreEquivalent(new[]{"effect","fight/000","fight/001","fight/002"},banks.Keys.ToArray());
        Assert.IsTrue(banks["fight/000"].Any(frame=>frame.LegacyId==16000));
        Assert.IsTrue(banks["fight/001"].Any(frame=>frame.LegacyId==16000));
        Assert.IsTrue(banks["effect"].All(frame=>frame.LegacyId>=13000));
        foreach(var bank in banks.Keys)
        {
            CollectionAssert.AreEquivalent(library.Sprites.Banks[bank].Keys.ToArray(),banks[bank].Select(frame=>frame.LegacyId).ToArray());
            foreach(var sprite in banks[bank]) Assert.IsTrue(sprite.Width==0 || library.Manifest.FileHashes.ContainsKey(sprite.Atlas));
        }
        CollectionAssert.AreEquivalent(library.Sprites.Banks["battlefield/000"].Keys.ToArray(),
            view.Document.Sprites.Select(frame=>frame.LegacyId).ToArray());
        Assert.AreSame(session.Story,view.Story); Assert.AreSame(session.Definition,view.Scene);
        CollectionAssert.AreEqual(filesBefore,Directory.GetFiles(library.Directory,"*",SearchOption.AllDirectories).Order().ToArray());
    }
}
