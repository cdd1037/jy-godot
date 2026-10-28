using System.Buffers.Binary;
using System.Text.Json;
using JinYong.Importing.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Battles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class LegacyProgressionImporterTests
{
    [TestMethod]
    public void ExtractedProgressionOwnsDataAndRoundTripsWithoutExecutableBytes()
    {
        var bytes = Fixture(); var reads = new List<string>();
        var result = LegacyProgressionImporter.Read(name => { reads.Add(name); return bytes; });
        CollectionAssert.AreEqual(new[] { "Z.DAT" }, reads);
        Assert.AreEqual(999, result.MaximumHp); Assert.AreEqual(999, result.MaximumMp);
        Assert.AreEqual(7, result.AptitudeBase); Assert.AreEqual(15, result.AptitudeStep);
        Assert.AreEqual(1, result.UnlearnedLevel); Assert.AreEqual(2, result.UnassociatedFirstLevel);
        Assert.AreEqual(8, result.RewardNumerator); Assert.AreEqual(10, result.RewardDenominator);
        Assert.AreEqual(42050, result.ExperienceThresholds[^1]); // proves unsigned table reads
        Array.Clear(bytes);
        Assert.AreEqual(42050, result.ExperienceThresholds[^1]);
        var json = JsonSerializer.Serialize(result, ContentJson.Options);
        var copy = JsonSerializer.Deserialize<ProgressionDefinition>(json, ContentJson.Options)!;
        CollectionAssert.AreEqual(result.ExperienceThresholds.ToArray(), copy.ExperienceThresholds.ToArray());
        Assert.IsTrue(json.Length < 2000);
    }

    [TestMethod]
    [DataRow(0x250EF)] [DataRow(0x250F2)] [DataRow(0x25101)]
    [DataRow(0x253B7)] [DataRow(0x253C9)] [DataRow(0x35557)] [DataRow(0x35570)]
    public void WrongInstructionLayoutOrDisagreeingConstantsAreRejected(int offset)
    {
        var bytes = Fixture(); bytes[offset] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyProgressionImporter.Read(bytes));
    }

    [TestMethod]
    public void InvalidThresholdsLimitsAndPracticeParametersAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyProgressionImporter.Read(new byte[100]));
        var bytes = Fixture(); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(LegacyProgressionImporter.ExperienceTableOffset + 2), 0);
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyProgressionImporter.Read(bytes));
        bytes = Fixture(); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x250F6), 0); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x25101), 0);
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyProgressionImporter.Read(bytes));
        bytes = Fixture(); bytes[0x34F1C] = 31;
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyProgressionImporter.Read(bytes));
        bytes = Fixture(); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x34F1E), 0);
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyProgressionImporter.Read(bytes));
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualDosExecutableSuppliesOriginalThirtyLevelAndPracticeRules()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to validate original assets.");
        var rules = LegacyProgressionImporter.Read(File.ReadAllBytes(Path.Combine(source!, "Z.DAT")));
        Assert.HasCount(30, rules.ExperienceThresholds);
        Assert.AreEqual(50, rules.ExperienceThresholds[1]); Assert.AreEqual(52000, rules.ExperienceThresholds[^1]);
        Assert.AreEqual(999, rules.MaximumHp); Assert.AreEqual(999, rules.MaximumMp);
        Assert.AreEqual(7, rules.AptitudeBase); Assert.AreEqual(15, rules.AptitudeStep);
        Assert.AreEqual(8, rules.RewardNumerator); Assert.AreEqual(10, rules.RewardDenominator);
    }

    private static byte[] Fixture()
    {
        var bytes = new byte[LegacyProgressionImporter.ExperienceTableOffset + 60];
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        void I16(int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), value);
        new byte[] { 0x66, 0x81, 0xB8 }.CopyTo(bytes, 0x250EF); U32(0x250F2, 0x70170); I16(0x250F6, 999);
        new byte[] { 0x66, 0xC7, 0x80 }.CopyTo(bytes, 0x250FA); U32(0x250FD, 0x70170); I16(0x25101, 999);
        new byte[] { 0x66, 0x81, 0xB8 }.CopyTo(bytes, 0x253B7); U32(0x253BA, 0x701A0); I16(0x253BE, 999);
        new byte[] { 0x66, 0xC7, 0x80 }.CopyTo(bytes, 0x253C2); U32(0x253C5, 0x701A0); I16(0x253C9, 999);
        bytes[0x35521] = 0xBB; bytes[0x35531] = 0xBA; bytes[0x35556] = 0xBB; bytes[0x35566] = 0xBA;
        bytes[0x3553B] = 0x8D; bytes[0x3553C] = 0x56; bytes[0x3553D] = 1; bytes[0x35570] = 1; bytes[0x35571] = 0xD2;
        bytes[0x34F1A] = 0xC1; bytes[0x34F1B] = 0xE0; bytes[0x34F1C] = 3; bytes[0x34F1D] = 0xBD;
        U32(0x35522, 15); U32(0x35557, 15); U32(0x35532, 7); U32(0x35567, 7); U32(0x34F1E, 10);
        for (var index = 0; index < 30; index++) I16(LegacyProgressionImporter.ExperienceTableOffset + index * 2, unchecked((short)(index * index * 50)));
        return bytes;
    }
}
