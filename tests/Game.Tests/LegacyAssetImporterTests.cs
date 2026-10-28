using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using JinYong.Content.Legacy;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Independently authored frame/archive fixtures, not original game screenshots or assets.
[TestClass]
public sealed class LegacyAssetImporterTests
{
    [TestMethod]
    public void AllBanksShareDecodedAssetsWhileAliasesAnchorsAndCoverageStayCorrect()
    {
        var pixels = Sprite(2, 0, [0, 2, 5, 6]);
        var alternateEncoding = Sprite(2, 0, [0, 1, 5, 0, 1, 6]);
        var shifted = Sprite(2, -3, [0, 2, 5, 6]);
        var opaqueZero = Sprite(1, 0, [0, 1, 0]);
        var transparentZero = Sprite(1, 0, []);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Scene(files, 0, [[], pixels, alternateEncoding, opaqueZero, transparentZero, shifted, []]);
        Scene(files, 1, [pixels, shifted]);
        Portraits(files, [pixels, [], shifted]);
        var originalHashes = files.ToDictionary(pair => pair.Key, pair => SHA256.HashData(pair.Value));
        var reads = new List<string>();
        var output = Output();
        var catalog = LegacyAssetImporter.Import(name => { reads.Add(name); return files[name]; }, files.Keys.ToArray(), output, Palette(), 2);

        Assert.HasCount(5, catalog.Assets);
        Assert.HasCount(3, catalog.Banks);
        Assert.HasCount(7, catalog.Banks["scene/000"]);
        Assert.HasCount(2, catalog.Banks["scene/001"]);
        Assert.HasCount(3, catalog.Banks["portrait"]);
        Assert.AreEqual(catalog.Banks["scene/000"][0], catalog.Banks["scene/000"][2]); // empty aliases forward
        Assert.AreEqual(catalog.Banks["scene/000"][2], catalog.Banks["scene/000"][4]); // distinct RLE, identical decoded pixels
        Assert.AreEqual(catalog.Banks["scene/000"][2], catalog.Banks["scene/001"][0]);
        Assert.AreEqual(catalog.Banks["scene/000"][2], catalog.Banks["portrait"][0]);
        Assert.AreNotEqual(catalog.Banks["scene/000"][2], catalog.Banks["scene/000"][10]); // anchor participates
        Assert.AreNotEqual(catalog.Banks["scene/000"][6], catalog.Banks["scene/000"][8]); // opaque color zero != transparent
        Assert.AreEqual(catalog.Banks["scene/000"][12], catalog.Banks["portrait"][1]); // portrait empty does not alias
        Assert.AreNotEqual(catalog.Banks["portrait"][1], catalog.Banks["portrait"][2]);
        Assert.AreEqual(-3, catalog.Assets[catalog.Banks["scene/000"][10]].OffsetX);
        Assert.AreEqual(0, catalog.Assets[catalog.Banks["portrait"][1]].Width);
        Assert.HasCount(6, reads);
        Assert.HasCount(6, reads.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.IsTrue(catalog.Assets.Keys.All(key => key.Length == 64 && key.All(character => char.IsAsciiHexDigitLower(character))));
        foreach (var pair in files) CollectionAssert.AreEqual(originalHashes[pair.Key], SHA256.HashData(pair.Value));

        var atlasPaths = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        Assert.HasCount(1, atlasPaths.Where(path => Path.GetExtension(path) == ".png").ToArray());
        Assert.IsTrue(atlasPaths.All(path => Path.GetRelativePath(output, path).Replace('\\', '/').StartsWith("assets/sprites/", StringComparison.Ordinal)));
        AssertCoverage(output, catalog.Assets[catalog.Banks["scene/000"][6]], 255);
        AssertCoverage(output, catalog.Assets[catalog.Banks["scene/000"][8]], 0);

        var local = catalog.Assets[catalog.Banks["scene/001"][2]] with { LegacyId = 2 };
        Assert.AreEqual(2, local.LegacyId);
        Assert.AreEqual(-3, local.OffsetX);
    }

    [TestMethod]
    public void ContentHashesAndAtlasAssignmentsAreIndependentOfFileListingOrder()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var sprite = Sprite(2, 0, [0, 2, 5, 6]);
        Scene(files, 0, [sprite]);
        Scene(files, 1, [Sprite(1, 0, [0, 1, 0]), sprite]);
        Portraits(files, [sprite]);
        var first = LegacyAssetImporter.Import(name => files[name], files.Keys.ToArray(), Output(), Palette(), 2);
        var second = LegacyAssetImporter.Import(name => files[name], files.Keys.Reverse().ToArray(), Output(), Palette(), 2);
        Assert.AreEqual(JsonSerializer.Serialize(first, ContentJson.Options), JsonSerializer.Serialize(second, ContentJson.Options));
        var json = JsonSerializer.Serialize(first, ContentJson.Options);
        var loaded = JsonSerializer.Deserialize<SharedSpriteCatalog>(json, ContentJson.Options)!;
        Assert.AreEqual(first.Banks["portrait"][0], loaded.Banks["portrait"][0]);
        Assert.AreEqual(first.Assets[first.Banks["scene/000"][0]], loaded.Assets[loaded.Banks["scene/000"][0]]);
    }

    [TestMethod]
    public void MissingRequiredSceneOrDiscoveredPairFailsBeforeReadingAndWriting()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Scene(files, 0, [Sprite(1, 0, [0, 1, 1])]);
        Portraits(files, [Sprite(1, 0, [0, 1, 2])]);
        var reads = 0;
        var output = Output();
        var missing = Assert.ThrowsExactly<FileNotFoundException>(() => LegacyAssetImporter.Import(name => { reads++; return files[name]; },
            files.Keys.ToArray(), output, Palette(), 2));
        Assert.AreEqual("SDX001", missing.FileName);
        Assert.AreEqual(0, reads);
        Assert.IsFalse(Directory.Exists(output));
        files.Add("smp099", []);
        var orphan = Assert.ThrowsExactly<FileNotFoundException>(() => LegacyAssetImporter.Import(name => { reads++; return files[name]; },
            files.Keys.ToArray(), output, Palette(), 1));
        Assert.AreEqual("SDX099", orphan.FileName);
        Assert.AreEqual(0, reads);
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public void AllDiscoveredScenesImportBeyondRequiredMetadataCountAndNamesIgnoreCase()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Scene(files, 0, [Sprite(1, 0, [0, 1, 1])]);
        Scene(files, 99, [Sprite(1, 0, [0, 1, 2])]);
        Portraits(files, [[]]);
        var lower = files.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value);
        var catalog = LegacyAssetImporter.Import(name => lower[name], lower.Keys.ToArray(), Output(), Palette(), 1);
        Assert.IsTrue(catalog.Banks.ContainsKey("scene/099"));
        Assert.HasCount(3, catalog.Assets);
        Assert.AreEqual(0, catalog.Assets[catalog.Banks["portrait"][0]].Width);
    }

    [TestMethod]
    public void InvalidFrameReportsBankAndLocalDoubledIdAndWritesNoAtlas()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Scene(files, 0, [Sprite(1, 0, [0, 1, 1]), new byte[2]]);
        Portraits(files, [[]]);
        var output = Output();
        var error = Assert.ThrowsExactly<InvalidDataException>(() => LegacyAssetImporter.Import(name => files[name], files.Keys.ToArray(), output, Palette(), 1));
        StringAssert.Contains(error.Message, "scene/000, frame 2");
        Assert.IsFalse(Directory.Exists(output));
    }

    private static void AssertCoverage(string output, SpriteAsset asset, byte expected)
    {
        var stem = Path.Combine(output, asset.Atlas[..^4]);
        var alpha = File.ReadAllBytes(stem + ".alpha");
        var indices = File.ReadAllBytes(stem + ".indices");
        Assert.AreEqual(expected, alpha[asset.Y * 1024 + asset.X]);
        Assert.AreEqual((byte)0, indices[asset.Y * 1024 + asset.X]);
    }

    private static string Output() => Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"), "assets");
    private static byte[] Palette() => new byte[768];

    private static byte[] Sprite(int width, short offsetX, byte[] row)
    {
        var bytes = new byte[9 + row.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), offsetX);
        bytes[8] = (byte)row.Length;
        row.CopyTo(bytes, 9);
        return bytes;
    }

    private static void Scene(Dictionary<string, byte[]> files, int id, byte[][] frames)
    {
        var ends = new uint[frames.Length];
        var length = 0;
        for (var index = 0; index < frames.Length; index++)
        {
            length += frames[index].Length;
            ends[index] = index == frames.Length - 1 ? 0 : (uint)length;
        }
        files.Add($"SDX{id:D3}", LegacyFormatTests.Index(ends));
        files.Add($"SMP{id:D3}", frames.SelectMany(bytes => bytes).ToArray());
    }

    private static void Portraits(Dictionary<string, byte[]> files, byte[][] frames)
    {
        var length = 0U;
        files.Add("HDGRP.IDX", LegacyFormatTests.Index(frames.Select(frame => { length += (uint)frame.Length; return length; }).ToArray()));
        files.Add("HDGRP.GRP", frames.SelectMany(bytes => bytes).ToArray());
    }
}
