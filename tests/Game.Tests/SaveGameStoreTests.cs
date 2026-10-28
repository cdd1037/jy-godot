using System.Security.Cryptography;
using System.Text.Json.Nodes;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Content.Persistence;
using JinYong.Core.Events;
using JinYong.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Independent save/envelope fixtures, not original DOS save files. Their minimal
// snapshots test storage only; Core Restore owns full state validation.
[TestClass]
public sealed class SaveGameStoreTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 10, 28, 12, 30, 0, TimeSpan.Zero);
    private static string Output() => Path.Combine(LegacyFixture.Root, ".local", "tests", Guid.NewGuid().ToString("N"), "saves");
    private static StoryDocument Overlay() => new()
    {
        SceneId = 70, Programs = [new(10001, [new(0, EventOperation.End, [])])],
        Dialogues = [new(10001, ["独立示例对白"])]
    };
    private static SaveGameDocument Document(string name = "主角的家", StoryDocument? overlay = null) => new()
    {
        Snapshot = new SessionSnapshot { SceneId = 70 }, LibraryFingerprint = new string('a', 64),
        StoryFingerprint = SaveGameIdentity.ForStory(overlay), Overlay = overlay, DisplayName = name, UtcSavedAt = SavedAt
    };

    [TestMethod]
    public void ThreeSlotsRoundTripActualOverlayContentAndListIndependentStatuses()
    {
        var store = new SaveGameStore(Output());
        Assert.HasCount(3, store.ListSlots()); Assert.IsTrue(store.ListSlots().All(slot => slot.Status == SaveSlotStatus.Empty));
        var overlay = Overlay(); store.Save(1, Document(overlay: overlay)); store.Save(3, Document("江湖"));
        var read = store.Read(1);
        Assert.AreEqual(70, read.Snapshot.SceneId); Assert.AreEqual(SavedAt, read.UtcSavedAt);
        Assert.AreEqual("独立示例对白", read.Overlay!.Dialogues[0].Pages[0]);
        Assert.AreEqual(10001, read.Overlay.Programs[0].Id);
        Assert.AreEqual(read.StoryFingerprint, SaveGameIdentity.ForStory(read.Overlay));
        Assert.AreNotSame(overlay, read.Overlay);
        CollectionAssert.AreEqual(new[] { SaveSlotStatus.Valid, SaveSlotStatus.Empty, SaveSlotStatus.Valid }, store.ListSlots().Select(slot => slot.Status).ToArray());
        Assert.AreEqual("江湖", store.ListSlots()[2].DisplayName);
        Assert.ThrowsExactly<FileNotFoundException>(() => store.Read(2));
    }

    [TestMethod]
    public void OverwriteKeepsPreviousCompleteSlotAsBackupAndNoTemporaryFiles()
    {
        var store = new SaveGameStore(Output()); store.Save(1, Document("旧存档"));
        var old = File.ReadAllBytes(store.GetSlotPath(1));
        store.Save(1, Document("新存档"));
        Assert.AreEqual("新存档", store.Read(1).DisplayName);
        CollectionAssert.AreEqual(old, File.ReadAllBytes(Path.Combine(store.Directory, "slot-1.backup.json")));
        Assert.HasCount(0, Directory.GetFiles(store.Directory, "*.tmp"));
    }

    [TestMethod]
    public void FailedAtomicReplacementDoesNotDamageExistingSlot()
    {
        var store = new SaveGameStore(Output()); store.Save(1, Document("保留原档"));
        var old = File.ReadAllBytes(store.GetSlotPath(1));
        Directory.CreateDirectory(Path.Combine(store.Directory, "slot-1.backup.json"));
        Assert.Throws<IOException>(() => store.Save(1, Document("不能提交")));
        CollectionAssert.AreEqual(old, File.ReadAllBytes(store.GetSlotPath(1)));
        Assert.AreEqual("保留原档", store.Read(1).DisplayName);
        Assert.HasCount(0, Directory.GetFiles(store.Directory, "*.tmp"));
    }

    [TestMethod]
    public void InvalidMetadataOrMismatchedOverlayNeverReplacesAValidSlot()
    {
        var store = new SaveGameStore(Output()); store.Save(1, Document());
        var old = File.ReadAllBytes(store.GetSlotPath(1));
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(1, Document() with { Overlay = Overlay() }));
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(1, Document() with { Snapshot = new SessionSnapshot { Version = 99 } }));
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(1, Document() with { LibraryFingerprint = "not-a-hash" }));
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(1, Document() with { UtcSavedAt = SavedAt.ToOffset(TimeSpan.FromHours(8)) }));
        CollectionAssert.AreEqual(old, File.ReadAllBytes(store.GetSlotPath(1)));
    }

    [TestMethod]
    public void TamperedPayloadChecksumAndUnknownSchemaAreInvalidSlots()
    {
        var store = new SaveGameStore(Output()); store.Save(1, Document()); store.Save(2, Document());
        var first = JsonNode.Parse(File.ReadAllText(store.GetSlotPath(1)))!;
        first["document"]!["display_name"] = "被修改"; File.WriteAllText(store.GetSlotPath(1), first.ToJsonString());
        var second = JsonNode.Parse(File.ReadAllText(store.GetSlotPath(2)))!;
        second["unknown_future_field"] = 1; File.WriteAllText(store.GetSlotPath(2), second.ToJsonString());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(1));
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(2));
        CollectionAssert.AreEqual(new[] { SaveSlotStatus.Invalid, SaveSlotStatus.Invalid, SaveSlotStatus.Empty }, store.ListSlots().Select(slot => slot.Status).ToArray());
    }

    [TestMethod]
    public void DuplicateJsonPropertiesMissingFieldsAndInvalidUtf8AreRejected()
    {
        var store = new SaveGameStore(Output()); store.Save(1, Document());
        var original = File.ReadAllText(store.GetSlotPath(1));
        File.WriteAllText(store.GetSlotPath(1), "{\"format_version\":1," + original[1..]);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(1));
        var node = JsonNode.Parse(original)!; node["document"]!.AsObject().Remove("snapshot");
        File.WriteAllText(store.GetSlotPath(1), node.ToJsonString());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(1));
        File.WriteAllBytes(store.GetSlotPath(1), [0xFF, 0xFE, 0xFF]);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(1));
    }

    [TestMethod]
    public void MissingDefaultSnapshotFieldsAreRejectedEvenWithTheirOriginalValidChecksum()
    {
        var store = new SaveGameStore(Output()); store.Save(1, Document());
        var original = File.ReadAllText(store.GetSlotPath(1));
        var node = JsonNode.Parse(original)!;
        node["document"]!["snapshot"]!.AsObject().Remove("steps");
        // Deserialization alone would restore default Steps=0, so the old
        // checksum would still match. The format schema must reject the omission.
        File.WriteAllText(store.GetSlotPath(1), node.ToJsonString());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(1));
        node = JsonNode.Parse(original)!;
        node["document"]!["snapshot"]!["position"]!.AsObject().Remove("x");
        File.WriteAllText(store.GetSlotPath(1), node.ToJsonString());
        Assert.AreEqual(SaveSlotStatus.Invalid, store.ListSlots()[0].Status);
    }

    [TestMethod]
    public void SizeLimitsAndSlotRangeAreCheckedBeforeUnboundedReadOrWrite()
    {
        var store = new SaveGameStore(Output()); Directory.CreateDirectory(store.Directory);
        using (var file = File.Create(store.GetSlotPath(1))) file.SetLength(SaveGameStore.MaximumSaveBytes + 1L);
        File.WriteAllBytes(store.GetSlotPath(2), []);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Read(1)); Assert.ThrowsExactly<InvalidDataException>(() => store.Read(2));
        foreach (var slot in new[] { -1, 0, 4, int.MaxValue })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => store.Read(slot));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => store.Save(slot, Document()));
        }
    }

    [TestMethod]
    public void SaveDirectoryCannotBeInsideLibraryOriginalResourcesOrExplicitProtectedRoot()
    {
        var root = Output(); var library = Path.Combine(root, "library");
        Directory.CreateDirectory(Path.Combine(library, "data")); Directory.CreateDirectory(Path.Combine(library, "assets"));
        File.WriteAllText(Path.Combine(library, "manifest.json"), "{}");
        Assert.ThrowsExactly<InvalidOperationException>(() => new SaveGameStore(Path.Combine(library, "saves")));
        var original = Path.Combine(root, "original"); Directory.CreateDirectory(original);
        File.WriteAllBytes(Path.Combine(original, "RANGER.IDX"), []); File.WriteAllBytes(Path.Combine(original, "RANGER.GRP"), []);
        Assert.ThrowsExactly<InvalidOperationException>(() => new SaveGameStore(Path.Combine(original, "saves")));
        var blocked = Path.Combine(root, "protected");
        Assert.ThrowsExactly<InvalidOperationException>(() => new SaveGameStore(Path.Combine(blocked, "nested"), blocked));
        Assert.IsFalse(Directory.Exists(Path.Combine(library, "saves"))); Assert.IsFalse(Directory.Exists(Path.Combine(original, "saves")));
        _ = new SaveGameStore(Path.Combine(root, "protected-neighbor"), blocked); // component boundary, not a string prefix
    }

    [TestMethod]
    public void LibraryIdentityIsOrderIndependentPortableAndChecksActualHashes()
    {
        var files = new Dictionary<string, byte[]> { ["data/a"] = [1, 2], ["assets/b"] = [3, 4] };
        string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        var first = new GameLibraryManifest { FileHashes = files.ToDictionary(pair => pair.Key, pair => Hash(pair.Value)),
            SourceHashes = new() { ["Z.DAT"] = new string('a', 64), ["RANGER.GRP"] = new string('b', 64) } };
        var reordered = first with { FileHashes = first.FileHashes.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value.ToUpperInvariant()),
            SourceHashes = first.SourceHashes.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value) };
        var identity = SaveGameIdentity.ForLibrary(first, path => files[path]);
        Assert.AreEqual(identity, SaveGameIdentity.ForLibrary(reordered, path => files[path]));
        Assert.AreNotEqual(identity, SaveGameIdentity.ForLibrary(first with { EngineApi = "2" }, path => files[path]));
        Assert.AreNotEqual(identity, SaveGameIdentity.ForLibrary(first with { Id = "different" }, path => files[path]));
        Assert.AreNotEqual(identity, SaveGameIdentity.ForLibrary(first with { SourceHashes = new() { ["Z.DAT"] = new string('c', 64) } }, path => files[path]));
        files["data/a"][0] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => SaveGameIdentity.ForLibrary(first, path => files[path]));
    }

    [TestMethod]
    public void StoryIdentityUsesActualContentAndIgnoresDictionaryInsertionOrder()
    {
        var first = Overlay() with { ItemNames = new() { [2] = "乙", [1] = "甲" } };
        var second = first with { ItemNames = first.ItemNames.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value) };
        Assert.AreEqual(SaveGameIdentity.ForStory(first), SaveGameIdentity.ForStory(second));
        Assert.AreNotEqual(SaveGameIdentity.ForStory(first), SaveGameIdentity.ForStory(first with { Dialogues = [new(10001, ["不同内容"])] }));
        Assert.AreNotEqual(SaveGameIdentity.ForStory(first), SaveGameIdentity.ForStory(null));
    }
}
