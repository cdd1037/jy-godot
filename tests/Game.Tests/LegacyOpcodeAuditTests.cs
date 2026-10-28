using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using JinYong.Content.Legacy;
using JinYong.Content.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class LegacyOpcodeAuditTests
{
    [TestMethod]
    public void AuditCompilesEveryNonemptyRecordAndRejectsWholeInvalidScripts()
    {
        // Independently authored fixture, not original resources.
        var source = CreateArchive([], Words(-1), Words(0, -1), Words(0, 68, -1), Words(1));
        var report = LegacyEventAudit.Read(source);
        Assert.AreEqual(5, report.ArchiveEntryCount);
        Assert.AreEqual(1, report.EmptyEntryCount);
        Assert.AreEqual(2, report.CompilerAcceptedCount);
        Assert.AreEqual(2, report.RejectedCount);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, report.Scripts.Select(script => script.Id).ToArray());
        Assert.AreEqual(2, report.Scripts[1].CommandCount);
        CollectionAssert.AreEqual(new[] { 0 }, report.Scripts[1].SourceOpcodes);
        Assert.IsTrue(report.Scripts.All(script => !script.ExecutionVerified));
        Assert.IsFalse(report.ExecutionVerified);
        Assert.IsFalse(report.KnownOriginalArchive);
        foreach (var rejected in report.Scripts.Where(script => !script.CompilerAccepted))
        {
            Assert.AreEqual(0, rejected.CommandCount);
            Assert.AreEqual(0, rejected.SourceOpcodes.Length);
            Assert.IsFalse(string.IsNullOrEmpty(rejected.FailureReason));
        }
    }

    [TestMethod]
    public void AuditHashesInputsDoesNotModifyThemAndSerializesOnlyMetadata()
    {
        var source = CreateArchive(Words(1, 123, 7, 0, -1));
        var before = Directory.GetFiles(source).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var report = LegacyEventAudit.Read(source);
        Assert.IsTrue(report.Scripts.Single().CompilerAccepted);
        foreach (var pair in before)
        {
            CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Path.Combine(source, pair.Key!)));
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(pair.Value)), report.SourceHashes[pair.Key!]);
        }
        var json = JsonSerializer.Serialize(report, ContentJson.Options);
        StringAssert.Contains(json, "compiler_accepted");
        StringAssert.Contains(json, "execution_verified");
        Assert.IsFalse(json.Contains("\"arguments\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"text\"", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(new[] { "KDEF.IDX", "KDEF.GRP" }, Directory.GetFiles(source).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public void Extended50DialectIsExplicitlyRefusedBeforeAnyDosInterpretation()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => LegacyEventAudit.Read("unused", LegacyScriptDialect.Extended50));
        Assert.ThrowsExactly<NotSupportedException>(() => LegacyEventAudit.Read("unused", (LegacyScriptDialect)42));
    }

    [TestMethod]
    public void CorruptArchiveIsNotPresentedAsAnEmptySuccessfulAudit()
    {
        var source = CreateArchive(Words(-1));
        File.WriteAllBytes(Path.Combine(source, "KDEF.IDX"), [255, 255, 255, 127]);
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyEventAudit.Read(source));
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualKdefAuditAccountsForEveryOriginalRecordWithoutClaimingExecution()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to audit original assets.");
        var report = LegacyEventAudit.Read(source!);
        Assert.IsTrue(report.KnownOriginalArchive);
        Assert.AreEqual(1018, report.ArchiveEntryCount);
        Assert.AreEqual(0, report.EmptyEntryCount);
        Assert.AreEqual(1018, report.CompilerAcceptedCount + report.RejectedCount);
        Assert.IsFalse(report.ExecutionVerified);
        Assert.IsTrue(report.Scripts.All(script => !script.ExecutionVerified));
    }

    private static byte[] Words(params short[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), words[index]);
        return bytes;
    }

    private static string CreateArchive(params byte[][] entries)
    {
        var source = Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"), "audit");
        Directory.CreateDirectory(source);
        using var group = new MemoryStream();
        var index = new byte[entries.Length * 4];
        for (var entry = 0; entry < entries.Length; entry++)
        {
            group.Write(entries[entry]);
            BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(entry * 4), (uint)group.Length);
        }
        File.WriteAllBytes(Path.Combine(source, "KDEF.IDX"), index);
        File.WriteAllBytes(Path.Combine(source, "KDEF.GRP"), group.ToArray());
        return source;
    }
}
