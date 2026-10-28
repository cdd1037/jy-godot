using System.Buffers.Binary;
using System.Text.Json;
using JinYong.Importing.Legacy;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class PartyStateTests
{
    private static RoleDefinition Role(int id, params (int Word, int Value)[] changes)
    {
        var words = new int[RoleWords.Count]; words[0] = id;
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
        words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
        foreach (var (word, value) in changes) words[word] = value;
        return new(id, $"Role {id}", "", words);
    }
    private static PartyDefinition Definition(params RoleDefinition[] roles) => new(roles, [0, -1, -1, -1, -1, -1]);

    [TestMethod]
    public void DefinitionsRoundTripAndRuntimeCannotChangeBaseline()
    {
        var definition = Definition(Role(0, (RoleWords.Hp, 8)), Role(1));
        var restored = JsonSerializer.Deserialize<PartyDefinition>(JsonSerializer.Serialize(definition))!;
        var a = new PartyState(restored); var b = new PartyState(restored);
        a.SetWord(0, RoleWords.Hp, 1234); a.Join(1);
        Assert.AreEqual(8, b.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(8, restored.Roles[0].Words[RoleWords.Hp]);
        Assert.IsFalse(b.Contains(1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => a.SetWord(0, RoleWords.Id, 2));
    }

    [TestMethod]
    public void JoinTransfersFourCarriedSlotsOnceAndDoesNotDuplicateBackpackEquipment()
    {
        var party = new PartyState(Definition(Role(0), Role(1,
            (RoleWords.TakingItemBegin, 7), (RoleWords.TakingCountBegin, 3),
            (RoleWords.TakingItemBegin + 3, 8), (RoleWords.TakingCountBegin + 3, 1),
            (RoleWords.EquipmentBegin, 12), (RoleWords.PracticeItem, 13), (RoleWords.ItemExperience, 80))));
        var join = party.Join(1);
        Assert.IsTrue(join.Joined);
        CollectionAssert.AreEqual(new[] { new PartyItemTransfer(7, 3), new PartyItemTransfer(8, 1) }, join.Items.ToArray());
        Assert.AreEqual(-1, party.GetWord(1, RoleWords.TakingItemBegin));
        Assert.AreEqual(0, party.GetWord(1, RoleWords.TakingCountBegin));
        Assert.AreEqual(-1, party.GetWord(1, RoleWords.EquipmentBegin));
        Assert.AreEqual(-1, party.GetWord(1, RoleWords.PracticeItem));
        Assert.AreEqual(0, party.GetWord(1, RoleWords.ItemExperience));
        Assert.IsFalse(party.Join(1).Joined);
        Assert.AreEqual(0, party.Join(1).Items.Count);
    }

    [TestMethod]
    public void FullPartyDoesNotConsumeRejectedRecruitsItemsAndLeaveCompactsSlots()
    {
        var roles = Enumerable.Range(0, 7).Select(i => Role(i, (RoleWords.TakingItemBegin, 7), (RoleWords.TakingCountBegin, 2))).ToArray();
        var party = new PartyState(Definition(roles));
        for (var i = 1; i <= 5; i++) Assert.IsTrue(party.Join(i).Joined);
        Assert.IsTrue(party.IsFull);
        Assert.IsFalse(party.Join(6).Joined);
        Assert.AreEqual(7, party.GetWord(6, RoleWords.TakingItemBegin));
        Assert.IsFalse(party.Leave(0));
        Assert.IsTrue(party.Leave(3));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 4, 5, -1 }, party.Members.ToArray());
        Assert.IsTrue(party.Join(6).Joined);
        party.DismissCompanions();
        CollectionAssert.AreEqual(new[] { 0, -1, -1, -1, -1, -1 }, party.Members.ToArray());
    }

    [TestMethod]
    public void LeaveReleasesItemOwnershipWithoutReturningItemsAgain()
    {
        var party = new PartyState(new PartyDefinition([Role(0), Role(1,
            (RoleWords.EquipmentBegin, 2), (RoleWords.EquipmentBegin + 1, 3),
            (RoleWords.PracticeItem, 4), (RoleWords.ItemExperience, 70))],
            [0, 1, -1, -1, -1, -1], new Dictionary<int, int> { [2] = 1, [3] = 1, [4] = 1 }));
        Assert.IsTrue(party.Leave(1));
        Assert.AreEqual(-1, party.ItemUsers[2]); Assert.AreEqual(-1, party.ItemUsers[3]); Assert.AreEqual(-1, party.ItemUsers[4]);
        Assert.AreEqual(-1, party.GetWord(1, RoleWords.PracticeItem));
        Assert.AreEqual(0, party.GetWord(1, RoleWords.ItemExperience));
    }

    [TestMethod]
    public void RestExcludesPoisonAndHurtAt33ButRecoversHurt32()
    {
        RoleDefinition Wounded(int id, int hurt, int poison) => Role(id, (RoleWords.Hurt, hurt), (RoleWords.Poison, poison),
            (RoleWords.Hp, 1), (RoleWords.MaximumHp, 80), (RoleWords.Mp, 2), (RoleWords.MaximumMp, 60), (RoleWords.PhysicalPower, 3));
        var party = new PartyState(new PartyDefinition([Wounded(0, 32, 0), Wounded(1, 33, 0), Wounded(2, 0, 1)],
            [0, 1, 2, -1, -1, -1]));
        CollectionAssert.AreEqual(new[] { 0 }, party.Rest().ToArray());
        Assert.AreEqual(80, party.GetWord(0, RoleWords.Hp)); Assert.AreEqual(60, party.GetWord(0, RoleWords.Mp));
        Assert.AreEqual(100, party.GetWord(0, RoleWords.PhysicalPower)); Assert.AreEqual(0, party.GetWord(0, RoleWords.Hurt));
        Assert.AreEqual(1, party.GetWord(1, RoleWords.Hp)); Assert.AreEqual(1, party.GetWord(2, RoleWords.Hp));
        party.ClearMp();
        Assert.AreEqual(0, party.GetWord(1, RoleWords.Mp)); Assert.AreEqual(0, party.GetWord(2, RoleWords.Mp));
    }

    [TestMethod]
    public void AttributeRulesClampFixedStatsRefillVitalsAndCheckArithmetic()
    {
        var party = new PartyState(Definition(Role(0, (RoleWords.Iq, 95), (RoleWords.Speed, 20), (RoleWords.Morality, 10),
            (RoleWords.Attack, 99), (RoleWords.MaximumHp, 100), (RoleWords.Hp, 20), (RoleWords.MaximumMp, 90), (RoleWords.Mp, 80))));
        Assert.AreEqual(5, party.AddAttribute(0, RoleWords.Iq, 200));
        Assert.AreEqual(-20, party.AddAttribute(0, RoleWords.Speed, -40));
        Assert.AreEqual(-10, party.AddAttribute(0, RoleWords.Morality, -80));
        Assert.AreEqual(300, party.AddAttribute(0, RoleWords.Attack, 300));
        Assert.AreEqual(120, party.AddAttribute(0, RoleWords.MaximumHp, 40));
        Assert.AreEqual(140, party.GetWord(0, RoleWords.Hp));
        Assert.AreEqual(30, party.AddAttribute(0, RoleWords.MaximumMp, 20));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => party.AddAttribute(0, RoleWords.MaximumHp, -200));
        Assert.AreEqual(140, party.GetWord(0, RoleWords.MaximumHp));
        party.SetWord(0, RoleWords.Fame, int.MaxValue);
        Assert.ThrowsExactly<OverflowException>(() => party.AddAttribute(0, RoleWords.Fame, 1));
        Assert.AreEqual(int.MaxValue, party.GetWord(0, RoleWords.Fame));
    }

    [TestMethod]
    public void MagicUsesFirstEmptySlotAndOriginalFullListFallback()
    {
        var party = new PartyState(Definition(Role(0, (RoleWords.MagicIdBegin, 8))));
        Assert.AreEqual(1, party.LearnMagic(0, 9));
        Assert.AreEqual(0, party.GetWord(0, RoleWords.MagicLevelBegin + 1));
        for (var slot = 2; slot < RoleWords.MagicCount; slot++) party.SetMagic(0, slot, slot + 20, 999);
        Assert.AreEqual(0, party.LearnMagic(0, 100));
        Assert.AreEqual(100, party.GetWord(0, RoleWords.MagicIdBegin));
        Assert.AreEqual(0, party.GetWord(0, RoleWords.MagicLevelBegin));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => party.SetMagic(0, 10, 1, 1));
    }

    [TestMethod]
    public void CarriedItemsUpdateExistingSlotsAndPreserveOriginalNonpositiveCounts()
    {
        var party = new PartyState(Definition(Role(0)));
        for (var i = 0; i < 4; i++) Assert.IsTrue(party.AddRoleItem(0, i, 1));
        Assert.IsFalse(party.AddRoleItem(0, 10, 1));
        Assert.IsTrue(party.AddRoleItem(0, 2, -3));
        Assert.AreEqual(2, party.GetWord(0, RoleWords.TakingItemBegin + 2));
        Assert.AreEqual(-2, party.GetWord(0, RoleWords.TakingCountBegin + 2));
    }

    [TestMethod]
    public void FemaleCheckOnlyExaminesActiveMembers()
    {
        var party = new PartyState(Definition(Role(0), Role(1, (RoleWords.Sex, 1))));
        Assert.IsFalse(party.HasFemale);
        party.Join(1); Assert.IsTrue(party.HasFemale);
        party.Leave(1); Assert.IsFalse(party.HasFemale);
        party.SetWord(0, RoleWords.Sex, 1); Assert.IsTrue(party.HasFemale);
    }

    [TestMethod]
    public void LegacyImporterUsesHeaderOffsetsAndPreservesRawSignedWords()
    {
        var header = new byte[836]; var roles = new byte[182 * 2]; var items = new byte[190 * 5];
        for (var slot = 1; slot < 6; slot++) BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan((12 + slot) * 2), -1);
        foreach (var role in new[] { Role(0, (RoleWords.Experience, -1)), Role(1, (RoleWords.Sex, 1)) })
            for (var word = 0; word < RoleWords.Count; word++)
                BinaryPrimitives.WriteInt16LittleEndian(roles.AsSpan(role.Id * 182 + word * 2), (short)role.Words[word]);
        roles[8] = (byte)'A'; roles[182 + 8] = (byte)'B'; roles[182 + 18] = (byte)'C';
        BinaryPrimitives.WriteInt16LittleEndian(items.AsSpan(4 * 190 + 76), 1);
        var definition = LegacyPartyImporter.Read(header, roles, items);
        Assert.AreEqual("A", definition.Roles[0].Name); Assert.AreEqual("B", definition.Roles[1].Name);
        Assert.AreEqual("C", definition.Roles[1].Nickname); Assert.AreEqual(1, definition.ItemUsers[4]);
        Assert.AreEqual(-1, definition.Roles[0].Words[RoleWords.Experience]);
        Assert.AreEqual(65535, new PartyState(definition).GetWord(0, RoleWords.Experience));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyPartyImporter.Read(new byte[834], roles, items));
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualRangerPartyImportsAllRolesAndSurvivesJsonRoundTrip()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to validate original assets.");
        var definition = LegacyPartyImporter.Read(LegacyArchive.Parse(
            File.ReadAllBytes(Path.Combine(source!, "RANGER.IDX")), File.ReadAllBytes(Path.Combine(source!, "RANGER.GRP"))));
        Assert.AreEqual(320, definition.Roles.Count);
        CollectionAssert.AreEqual(new[] { 0, -1, -1, -1, -1, -1 }, definition.Members.ToArray());
        Assert.AreEqual(200, definition.ItemUsers.Count);
        Assert.IsTrue(definition.Roles.All(role => role.Name.Length > 0));
        var restored = JsonSerializer.Deserialize<PartyDefinition>(JsonSerializer.Serialize(definition))!;
        foreach (var role in definition.Roles)
        {
            Assert.AreEqual(role.Name, restored.Roles[role.Id].Name);
            CollectionAssert.AreEqual(role.Words.ToArray(), restored.Roles[role.Id].Words.ToArray());
        }
        var state = new PartyState(restored);
        Assert.IsTrue(state.Contains(0)); Assert.IsFalse(state.IsFull);
    }
}
