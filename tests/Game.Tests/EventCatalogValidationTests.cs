using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class EventCatalogValidationTests
{
    private static PartyDefinition Party(int carriedItem = -1)
    {
        RoleDefinition Role(int id)
        {
            var words = new int[RoleWords.Count]; words[0] = id;
            for (var slot = 0; slot < RoleWords.TakingItemCount; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
            words[RoleWords.TakingItemBegin] = carriedItem;
            return new(id, $"Role {id}", "", words);
        }
        return new([Role(0), Role(1)], [0, -1, -1, -1, -1, -1]);
    }
    private static StoryCatalog Catalog(EventOperation operation, int[] args, PartyDefinition? party = null)
    {
        var program = new EventProgram(1, [new(0, operation, args), new(1, EventOperation.End, [])]);
        return new(70, null, 0, null, Direction.Down, [program], [],
            new Dictionary<int, string> { [0] = "Zero ID item", [1] = "Item" }, new Dictionary<int, int>(), party: party);
    }

    [TestMethod]
    public void KnownRolesAreRequiredWhenPartyDefinitionsExist()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.JoinParty, [9], Party()));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.SetRoleAttribute, [9, RoleWords.Hp, 5], Party()));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.BranchParty, [9, 1, 1], Party()));
        _ = Catalog(EventOperation.JoinParty, [1], Party());
        _ = Catalog(EventOperation.BranchParty, [0, 1, 1], Party());
    }

    [TestMethod]
    public void MissingPartyAllowsPartialPackagesButDoesNotAllowMalformedCommands()
    {
        _ = Catalog(EventOperation.JoinParty, [1]);
        _ = Catalog(EventOperation.RestParty, []);
        _ = Catalog(EventOperation.AddFame, [1]);
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.JoinParty, [-1]));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.SetRoleAttribute, [1, 8, 10]));
    }

    [TestMethod]
    public void WritableAttributeWordsExcludeIdentityAndEncodedNames()
    {
        foreach (var word in new[] { -1, 0, 1, 4, 8, 13, 91 })
            Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.SetRoleAttribute, [1, word, 2], Party()));
        _ = Catalog(EventOperation.SetRoleAttribute, [1, RoleWords.Sex, 1], Party());
        _ = Catalog(EventOperation.AddRoleAttribute, [1, RoleWords.Fame, 5], Party());
        _ = Catalog(EventOperation.BranchRole, [1, RoleWords.HeadId, 0, 9, 1, 1], Party());
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.BranchRole, [1, RoleWords.Hp, 10, 9, 1, 1], Party()));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.NoticeRole, [1, 8], Party()));
    }

    [TestMethod]
    public void JoiningAndCarriedItemCommandsValidateItemReferencesIncludingZeroId()
    {
        _ = Catalog(EventOperation.JoinParty, [1], Party(0));
        _ = Catalog(EventOperation.RoleAddItem, [1, 0, 0], Party());
        _ = Catalog(EventOperation.RoleAddItem, [1, 1, -2], Party());
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.JoinParty, [1], Party(99)));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.JoinParty, [1], Party(-2)));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.RoleAddItem, [1, 99, 1], Party()));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.BranchItemExists, [99, 1, 1], Party()));
    }

    [TestMethod]
    public void MagicSlotsAndValuesAreCheckedWithoutInventingMagicDefinitions()
    {
        _ = Catalog(EventOperation.LearnMagic, [1, 1234, 0], Party());
        _ = Catalog(EventOperation.SetMagic, [1, -1, 1, 0], Party());
        _ = Catalog(EventOperation.SetMagic, [1, 9, 1, 999], Party());
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.LearnMagic, [1, -1, 0], Party()));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.SetMagic, [1, 10, 1, 1], Party()));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.SetMagic, [1, -2, 1, 1], Party()));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.SetMagic, [1, 0, 1, -1], Party()));
    }

    [TestMethod]
    public void WorldOperationsAcceptDeferredDependenciesButRejectInvalidSelectors()
    {
        _ = Catalog(EventOperation.IncrementEventScripts, [-2, -2, 1, 2, 3]);
        _ = Catalog(EventOperation.ReplaceTiles, [999, 0, -1, 100]);
        _ = Catalog(EventOperation.OpenScene, [999]);
        _ = Catalog(EventOperation.OpenAllScenes, []);
        _ = Catalog(EventOperation.BranchBooks, [1, 1]);
        _ = Catalog(EventOperation.MoveShop, []);
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.IncrementEventScripts, [-1, 0, 1, 2, 3]));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.IncrementEventScripts, [0, 200, 1, 2, 3]));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.ReplaceTiles, [0, 6, 1, 2]));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.ReplaceTiles, [0, 0, 1, 32768]));
        Assert.ThrowsExactly<ArgumentException>(() => Catalog(EventOperation.OpenScene, [-2]));
    }
}
