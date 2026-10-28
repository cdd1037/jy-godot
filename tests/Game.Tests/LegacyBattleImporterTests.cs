using System.Buffers.Binary;
using System.Text.Json;
using JinYong.Importing.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Battles;
using JinYong.Core.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class LegacyBattleImporterTests
{
    [TestMethod]
    public void ModelsPreserveRawWordsOwnCopiesAndRoundTripJson()
    {
        var words = new int[93]; words[0] = 777; words[6] = 3; words[7] = 150; words[8] = 5;
        words[9] = -1; words[15] = 2; words[21] = 32; words[27] = 20;
        words[33] = 1; words[53] = 21; words[73] = 23;
        var battle = new BattleDefinition(0, words);
        words[7] = 999;
        Assert.AreEqual(777, battle[0]); // archive index is authoritative, not raw word zero
        Assert.AreEqual(150, battle.Experience);
        Assert.AreEqual(3, battle.BattlefieldId);
        Assert.AreEqual(5, battle.MusicId);
        Assert.AreEqual(-1, battle.PresetParty(0)); Assert.AreEqual(2, battle.FixedParty(0));
        Assert.AreEqual(new GridPosition(32, 20), battle.PartyPosition(0));
        Assert.AreEqual(1, battle.EnemyRole(0)); Assert.AreEqual(new GridPosition(21, 23), battle.EnemyPosition(0));
        var layers = new short[8192]; layers[3 * 64 + 4] = 100; layers[4096 + 3 * 64 + 4] = 200;
        var field = new BattlefieldDefinition(0, layers); layers[3 * 64 + 4] = 300;
        Assert.AreEqual((short)100, field.At(BattlefieldLayer.Ground, new(4, 3)));
        Assert.AreEqual((short)200, field.At(BattlefieldLayer.Building, new(4, 3)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => battle.EnemyRole(20));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => field.At(BattlefieldLayer.Ground, new(64, 0)));
        var magicWords = new int[68]; magicWords[6] = short.MinValue; magicWords[11] = 5;
        var magic = new MagicDefinition(0, "武功", magicWords); magicWords[11] = 10;
        var itemWords = new int[95]; itemWords[52] = 20;
        var item = new ItemDefinition(0, "武器", itemWords); itemWords[52] = 40;
        Assert.AreEqual(5, magic[MagicWords.SoundId]); Assert.AreEqual(20, item[ItemWords.AddAttack]);
        Assert.AreEqual(short.MinValue, magic[6]);
        var copied = JsonSerializer.Deserialize<BattleDefinition>(JsonSerializer.Serialize(battle, ContentJson.Options), ContentJson.Options)!;
        CollectionAssert.AreEqual(battle.Words.ToArray(), copied.Words.ToArray());
        var copiedMagic = JsonSerializer.Deserialize<MagicDefinition>(JsonSerializer.Serialize(magic, ContentJson.Options), ContentJson.Options)!;
        CollectionAssert.AreEqual(magic.Words.ToArray(), copiedMagic.Words.ToArray());
        var copiedItem = JsonSerializer.Deserialize<ItemDefinition>(JsonSerializer.Serialize(item, ContentJson.Options), ContentJson.Options)!;
        CollectionAssert.AreEqual(item.Words.ToArray(), copiedItem.Words.ToArray());
        var copiedField = JsonSerializer.Deserialize<BattlefieldDefinition>(JsonSerializer.Serialize(field, ContentJson.Options), ContentJson.Options)!;
        CollectionAssert.AreEqual(field.Layers.ToArray(), copiedField.Layers.ToArray());
    }

    [TestMethod]
    public void BattleImporterUsesReferencedFieldAndRangerSectionsWithoutReadingExecutable()
    {
        var battle = new short[93]; battle[6] = 1; battle[7] = 150;
        var first = new short[8192]; var second = new short[8192]; second[4096 + 4 * 64 + 3] = 20;
        var archive = Archive(Bytes(first), Bytes(second));
        var reads = new List<string>();
        var files = new Dictionary<string, byte[]> { ["WAR.STA"] = Bytes(battle),
            ["WARFLD.IDX"] = Index(16384, 32768), ["WARFLD.GRP"] = [.. Bytes(first), .. Bytes(second)] };
        var result = LegacyBattleImporter.Read(name => { reads.Add(name); return files[name]; }, Ranger());
        Assert.HasCount(1, result.Battles); Assert.HasCount(2, result.Fields);
        Assert.HasCount(1, result.Magics); Assert.HasCount(1, result.Items);
        Assert.AreEqual(1, result.Battles[0].BattlefieldId);
        Assert.AreEqual((short)20, result.Fields[1].At(BattlefieldLayer.Building, new(3, 4)));
        CollectionAssert.AreEquivalent(new[] { "WAR.STA", "WARFLD.IDX", "WARFLD.GRP" }, reads);
        Assert.AreEqual(2, LegacyBattleImporter.ReadFields(archive).Count);
    }

    [TestMethod]
    public void InvalidRecordLengthsIdsAndMissingFieldReferencesAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyBattleImporter.ReadBattles(new byte[185]));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyBattleImporter.ReadFields(Archive(new byte[16382])));
        var badMagic = new short[68]; badMagic[0] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyBattleImporter.ReadMagics(Ranger(magic: Bytes(badMagic))));
        var badItem = new short[95]; badItem[0] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyBattleImporter.ReadItems(Ranger(item: Bytes(badItem))));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyBattleImporter.ReadMagics(Ranger(magic: new byte[134])));
        var battle = new short[93]; battle[6] = 2;
        var files = new Dictionary<string, byte[]> { ["WAR.STA"] = Bytes(battle), ["WARFLD.IDX"] = Index(16384), ["WARFLD.GRP"] = new byte[16384] };
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyBattleImporter.Read(name => files[name], Ranger()));
        Assert.ThrowsExactly<ArgumentException>(() => new MagicDefinition(0, "", new int[67]));
        var outside = new int[95]; outside[52] = 32768;
        Assert.ThrowsExactly<ArgumentException>(() => new ItemDefinition(0, "", outside));
    }

    [TestMethod]
    public void FieldTailsArePreservedInTheArchiveAndDoNotShiftFollowingFieldLayers()
    {
        var first = new short[12288]; first[0] = 100; first[4096] = 200; Array.Fill(first, (short)999, 8192, 4096);
        var second = new short[8192]; second[0] = 300; second[4096] = 400;
        var third = new short[12288]; third[0] = 500; third[4096] = 600; Array.Fill(third, (short)888, 8192, 4096);
        var archive = Archive(Bytes(first), Bytes(second), Bytes(third));
        var result = LegacyBattleImporter.ReadFields(archive);
        Assert.HasCount(3, result);
        Assert.AreEqual(24576, archive.Entry(0).Length); Assert.AreEqual(24576, archive.Entry(2).Length);
        foreach (var field in result) Assert.HasCount(8192, field.Layers);
        Assert.AreEqual((short)100, result[0].At(BattlefieldLayer.Ground, new(0, 0)));
        Assert.AreEqual((short)200, result[0].At(BattlefieldLayer.Building, new(0, 0)));
        Assert.AreEqual((short)300, result[1].At(BattlefieldLayer.Ground, new(0, 0)));
        Assert.AreEqual((short)400, result[1].At(BattlefieldLayer.Building, new(0, 0)));
        Assert.AreEqual((short)500, result[2].At(BattlefieldLayer.Ground, new(0, 0)));
        Assert.AreEqual((short)600, result[2].At(BattlefieldLayer.Building, new(0, 0)));
        Assert.AreEqual((short)0, result[0].Layers[^1]); Assert.AreEqual((short)0, result[2].Layers[^1]);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualDosBattleZeroAndAllRawDefinitionCountsImport()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to validate original assets.");
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(source!, name));
        var result = LegacyBattleImporter.Read(Read, LegacyArchive.Parse(Read("RANGER.IDX"), Read("RANGER.GRP")));
        Assert.HasCount(140, result.Battles); Assert.HasCount(26, result.Fields);
        Assert.HasCount(93, result.Magics); Assert.HasCount(200, result.Items);
        var battle = result.Battles[0];
        Assert.AreEqual(0, battle.BattlefieldId); Assert.AreEqual(150, battle.Experience); Assert.AreEqual(5, battle.MusicId);
        Assert.AreEqual(0, battle.PresetParty(0)); Assert.AreEqual(-1, battle.FixedParty(0));
        Assert.AreEqual(new GridPosition(32, 20), battle.PartyPosition(0));
        Assert.AreEqual(1, battle.EnemyRole(0)); Assert.AreEqual(new GridPosition(21, 23), battle.EnemyPosition(0));
        Assert.AreEqual(1, result.Magics[1].Id); Assert.IsFalse(string.IsNullOrWhiteSpace(result.Magics[67].Name));
    }

    private static LegacyArchive Ranger(byte[]? magic = null, byte[]? item = null) => Archive(new byte[836], new byte[182], item ?? new byte[190], new byte[52], magic ?? new byte[136], new byte[30]);
    private static LegacyArchive Archive(params byte[][] entries)
    {
        uint end = 0;
        return LegacyArchive.Parse(Index(entries.Select(bytes => { end += (uint)bytes.Length; return end; }).ToArray()), entries.SelectMany(bytes => bytes).ToArray());
    }
    private static byte[] Bytes(short[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), words[index]);
        return bytes;
    }
    private static byte[] Index(params uint[] ends)
    {
        var bytes = new byte[ends.Length * 4];
        for (var index = 0; index < ends.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), ends[index]);
        return bytes;
    }
}
