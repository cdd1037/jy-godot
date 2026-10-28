using System.Buffers.Binary;
using JinYong.Importing.Legacy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Independently authored MMAP fixtures; no original game resources are copied here.
[TestClass]
public sealed class WorldAssetTests
{
    [TestMethod]
    public void WorldUsesDoubledIdsAndOrdinaryArchiveEmptyFramesWithoutAliases()
    {
        var files = Files();
        var first = Sprite(1);
        var last = Sprite(2);
        files.Add("MMAP.IDX", Index((uint)first.Length, (uint)first.Length, (uint)(first.Length + last.Length), 0, 0));
        files.Add("MMAP.GRP", [.. first, .. last]);
        var result = LegacyAssetImporter.Import(name => files[name], files.Keys.ToArray(), Output(), new byte[768], 1);
        var world = result.Banks["world"];
        Assert.HasCount(3, world); // trailing zero padding is not an SDX sentinel frame
        CollectionAssert.AreEqual(new[] { 0, 2, 4 }, world.Keys.Order().ToArray());
        Assert.AreEqual(0, result.Assets[world[2]].Width);
        Assert.AreEqual(1, result.Assets[world[4]].Width);
        Assert.AreNotEqual(world[2], world[4]);
        Assert.AreEqual(result.Banks["scene/000"][0], world[0]);
        Assert.AreEqual(result.Banks["portrait"][0], world[0]);
        Assert.AreNotEqual(world[0], world[4]);
        Assert.HasCount(3, result.Assets);
        Assert.IsTrue(result.Assets.Values.Where(asset => asset.Width > 0).All(asset => asset.Atlas.StartsWith("assets/sprites/", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("MMAP.IDX", "MMAP.GRP")]
    [DataRow("MMAP.GRP", "MMAP.IDX")]
    public void HalfWorldPairIsRejectedBeforeReadingResourcesOrWritingAtlas(string present, string missing)
    {
        var files = Files(); files.Add(present, []);
        var output = Output();
        var reads = 0;
        var error = Assert.ThrowsExactly<FileNotFoundException>(() => LegacyAssetImporter.Import(name => { reads++; return files[name]; },
            files.Keys.ToArray(), output, new byte[768], 1));
        Assert.AreEqual(missing, error.FileName);
        Assert.AreEqual(0, reads);
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public void SceneOnlyFixtureMayOmitBothWorldFiles()
    {
        var files = Files();
        var result = LegacyAssetImporter.Import(name => files[name], files.Keys.ToArray(), Output(), new byte[768], 1);
        Assert.IsFalse(result.Banks.ContainsKey("world"));
        Assert.HasCount(2, result.Banks);
    }

    [TestMethod]
    public void WorldFileDiscoveryIsCaseInsensitiveAndMalformedWorldFrameNamesItsBank()
    {
        var files = Files(); files.Add("mmap.idx", Index(2)); files.Add("mmap.grp", [0, 0]);
        var output = Output();
        var error = Assert.ThrowsExactly<InvalidDataException>(() => LegacyAssetImporter.Import(name => files[name], files.Keys.ToArray(), output, new byte[768], 1));
        StringAssert.Contains(error.Message, "world, frame 0");
        Assert.IsFalse(Directory.Exists(output));
    }

    private static Dictionary<string, byte[]> Files()
    {
        var sprite = Sprite(1);
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["SDX000"] = Index(0), ["SMP000"] = sprite,
            ["HDGRP.IDX"] = Index((uint)sprite.Length), ["HDGRP.GRP"] = sprite
        };
    }

    private static byte[] Sprite(byte color) => [1, 0, 1, 0, 0, 0, 0, 0, 3, 0, 1, color];
    private static string Output() => Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"), "world-assets");
    private static byte[] Index(params uint[] ends)
    {
        var bytes = new byte[ends.Length * 4];
        for (var index = 0; index < ends.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), ends[index]);
        return bytes;
    }
}
