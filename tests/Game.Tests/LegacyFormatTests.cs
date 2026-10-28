using System.Buffers.Binary;
using JinYong.Importing.Legacy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class LegacyFormatTests
{
    internal static byte[] Index(params uint[] ends)
    {
        var bytes = new byte[ends.Length * 4];
        for (var i = 0; i < ends.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), ends[i]);
        return bytes;
    }

    [TestMethod]
    public void CumulativeArchivePreservesEmptyRecordsAndZeroTail()
    {
        var archive = LegacyArchive.Parse(Index(0, 2, 2, 3, 0, 0), [10, 11, 12]);
        Assert.AreEqual(4, archive.Count);
        Assert.AreEqual(0, archive.Entry(0).Length);
        CollectionAssert.AreEqual(new byte[] { 10, 11 }, archive.Entry(1).ToArray());
        Assert.AreEqual(0, archive.Entry(2).Length);
        CollectionAssert.AreEqual(new byte[] { 12 }, archive.Entry(3).ToArray());
    }

    [TestMethod]
    public void SentinelLastEntryExtendsToEofAndEmptyPointersAlias()
    {
        var archive = LegacyArchive.Parse(Index(1, 1, 3, 0), [10, 20, 21, 30], true);
        Assert.AreEqual(4, archive.Count);
        CollectionAssert.AreEqual(new byte[] { 20, 21 }, archive.Entry(1, aliasEmpty: true).ToArray());
        CollectionAssert.AreEqual(new byte[] { 30 }, archive.Entry(3).ToArray());
    }

    [TestMethod]
    public void SceneIndexWithoutSentinelGetsAnEofEntry()
    {
        var archive = LegacyArchive.Parse(Index(1, 2), [10, 20, 30], true);
        Assert.AreEqual(3, archive.Count);
        CollectionAssert.AreEqual(new byte[] { 30 }, archive.Entry(2).ToArray());
    }

    [TestMethod]
    public void BrokenIndexesAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyArchive.Parse([0], []));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyArchive.Parse(Index(2, 1), [1, 2]));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyArchive.Parse(Index(1), [1, 2]));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyArchive.Parse(Index(1, 0, 1), [1]));
    }

    [TestMethod]
    public void RleKeepsSignedAnchorsAndOpaquePaletteZero()
    {
        // 3x1, anchor -2,+5; skip one transparent pixel then draw indices 0,63.
        var sprite = LegacySpriteDecoder.Decode([3, 0, 1, 0, 254, 255, 5, 0, 4, 1, 2, 0, 63]);
        Assert.AreEqual(-2, sprite.OffsetX);
        Assert.AreEqual(5, sprite.OffsetY);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 63 }, sprite.Indices);
        CollectionAssert.AreEqual(new byte[] { 0, 255, 255 }, sprite.Alpha);
    }

    [TestMethod]
    public void EmptyFrameHeaderIsAValidTransparentSprite()
    {
        var sprite = LegacySpriteDecoder.Decode(new byte[8]);
        Assert.AreEqual(0, sprite.Width);
        Assert.AreEqual(0, sprite.Height);
        Assert.IsEmpty(sprite.Indices);
    }

    [TestMethod]
    public void TruncatedRowsOverwideRunsAndTrailingBytesAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => LegacySpriteDecoder.Decode([1, 0, 1, 0, 0, 0, 0, 0]));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacySpriteDecoder.Decode([1, 0, 1, 0, 0, 0, 0, 0, 4, 0, 2, 1, 2]));
        Assert.ThrowsExactly<InvalidDataException>(() => LegacySpriteDecoder.Decode([1, 0, 1, 0, 0, 0, 0, 0, 3, 0, 1, 1, 99]));
    }

    [TestMethod]
    public void VgaChannelsExpandByBitReplication()
    {
        var palette = new byte[768];
        palette[0] = 63; palette[1] = 16;
        var rgb = LegacySpriteDecoder.ReadPalette(palette);
        Assert.AreEqual((byte)255, rgb[0]);
        Assert.AreEqual((byte)65, rgb[1]);
        Assert.AreEqual((byte)0, rgb[2]);
        palette[2] = 64;
        Assert.ThrowsExactly<InvalidDataException>(() => LegacySpriteDecoder.ReadPalette(palette));
    }
}
