using System.Buffers.Binary;
using JinYong.Content.Legacy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Independently authored RLE/archive fixtures. These verify native format
// boundaries without importing or duplicating original game resource banks.
[TestClass]
public sealed class BattleAssetTests
{
    [TestMethod]
    public void BattlefieldEofFrameAndEffectFightPointerIdsShareDecodedAtlasAssets()
    {
        var first = Sprite(0, [0, 2, 9, 8]);
        var alternativeEncoding = Sprite(0, [0, 1, 9, 0, 1, 8]);
        var shifted = Sprite(1, [0, 2, 9, 8]);
        var files = Files(first);
        // WDX's last zero is a sentinel: its final frame is the remaining WMP
        // data, not an empty slot. The middle zero-width frame must stay empty.
        files.Add("WDX000", Index((uint)first.Length, (uint)first.Length, 0));
        files.Add("WMP000", [.. first, .. shifted]);
        // Ordinary IDX tail zeros do not add frames, and ordinary empty entries
        // never borrow the next image. Pointer bank bases differ from WDX IDs.
        files.Add("EFT.IDX", Index((uint)first.Length, (uint)first.Length, (uint)(first.Length + shifted.Length), 0));
        files.Add("EFT.GRP", [.. first, .. shifted]);
        files.Add("FIGHT007.IDX", Index((uint)alternativeEncoding.Length, (uint)alternativeEncoding.Length, (uint)(alternativeEncoding.Length + shifted.Length)));
        files.Add("FIGHT007.GRP", [.. alternativeEncoding, .. shifted]);
        var output = Output();
        var result = LegacyAssetImporter.Import(name => files[name], files.Keys.ToArray(), output, new byte[768], 1);
        var field = result.Banks["battlefield/000"];
        var effects = result.Banks["effect"];
        var fight = result.Banks["fight/007"];
        CollectionAssert.AreEqual(new[] { 0, 2, 4 }, field.Keys.Order().ToArray());
        CollectionAssert.AreEqual(new[] { 13000, 13002, 13004 }, effects.Keys.Order().ToArray());
        CollectionAssert.AreEqual(new[] { 16000, 16002, 16004 }, fight.Keys.Order().ToArray());
        var common = result.Banks["scene/000"][0];
        Assert.AreEqual(common, result.Banks["portrait"][0]);
        Assert.AreEqual(common, field[0]); Assert.AreEqual(common, effects[13000]); Assert.AreEqual(common, fight[16000]);
        Assert.AreEqual(field[2], effects[13002]); Assert.AreEqual(field[2], fight[16002]);
        Assert.AreEqual(0, result.Assets[field[2]].Width);
        Assert.AreEqual(field[4], effects[13004]); Assert.AreEqual(field[4], fight[16004]);
        Assert.AreEqual(1, result.Assets[field[4]].OffsetX);
        Assert.AreEqual(2, result.Assets[field[4]].Width); // EOF contains a real frame
        Assert.AreNotEqual(field[2], field[4]); Assert.AreNotEqual(common, field[4]);
        Assert.HasCount(3, result.Assets); // decoded duplicate RLE + independent anchor + empty
        var packed = result.Assets.Values.Where(asset => asset.Width > 0).ToArray();
        Assert.HasCount(1, packed.Select(asset => asset.Atlas).Distinct().ToArray());
        Assert.HasCount(1, Directory.GetFiles(output, "*.png", SearchOption.AllDirectories));
        Assert.IsTrue(Directory.GetFiles(output, "*", SearchOption.AllDirectories).All(path =>
            Path.GetRelativePath(output, path).Replace('\\', '/').StartsWith("assets/sprites/", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("WDX012", "WMP012")]
    [DataRow("WMP012", "WDX012")]
    [DataRow("EFT.IDX", "EFT.GRP")]
    [DataRow("EFT.GRP", "EFT.IDX")]
    [DataRow("FIGHT007.IDX", "FIGHT007.GRP")]
    [DataRow("FIGHT007.GRP", "FIGHT007.IDX")]
    public void AnyHalfBattlePairFailsBeforeReadingOrWriting(string present, string missing)
    {
        var files = Files(Sprite(0, [0, 2, 9, 8]));
        files.Add(present.ToLowerInvariant(), []);
        var reads = 0; var output = Output();
        var error = Assert.ThrowsExactly<FileNotFoundException>(() => LegacyAssetImporter.Import(name =>
            { reads++; return files[name]; }, files.Keys.ToArray(), output, new byte[768], 1));
        Assert.AreEqual(missing, error.FileName);
        Assert.AreEqual(0, reads);
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    [DataRow("WDX000", "WMP000", "battlefield/000", 2)]
    [DataRow("EFT.IDX", "EFT.GRP", "effect", 13002)]
    [DataRow("FIGHT007.IDX", "FIGHT007.GRP", "fight/007", 16002)]
    public void MalformedLastFrameReportsItsActualBankPointerAndLeavesNoAtlas(string indexName, string groupName, string bank, int localId)
    {
        var first = Sprite(0, [0, 2, 9, 8]); var files = Files(first);
        files.Add(indexName, Index((uint)first.Length, indexName.StartsWith("WDX", StringComparison.Ordinal) ? 0 : (uint)first.Length + 2));
        files.Add(groupName, [.. first, 0, 0]);
        var output = Output();
        var error = Assert.ThrowsExactly<InvalidDataException>(() => LegacyAssetImporter.Import(name => files[name], files.Keys.ToArray(), output, new byte[768], 1));
        StringAssert.Contains(error.Message, $"{bank}, frame {localId}");
        Assert.IsFalse(Directory.Exists(output));
    }

    private static Dictionary<string, byte[]> Files(byte[] sprite) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["SDX000"] = Index(0), ["SMP000"] = sprite,
        ["HDGRP.IDX"] = Index((uint)sprite.Length), ["HDGRP.GRP"] = sprite
    };
    private static byte[] Sprite(short offsetX, byte[] row)
    {
        var bytes = new byte[9 + row.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), offsetX);
        bytes[8] = (byte)row.Length; row.CopyTo(bytes, 9); return bytes;
    }
    private static string Output() => Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"), "battle-assets");
    private static byte[] Index(params uint[] ends)
    {
        var bytes = new byte[ends.Length * 4];
        for (var index = 0; index < ends.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), ends[index]);
        return bytes;
    }
}
