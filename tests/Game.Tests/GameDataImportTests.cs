using System.Text.Json;
using JinYong.Importing.Legacy;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class GameDataImportTests
{
    private static readonly Lazy<GameDataDocument> Original = new(ReadOriginal);
    private static GameDataDocument ReadOriginal()
    {
        var source = Environment.GetEnvironmentVariable("JINYONG_ORIGINAL_DATA");
        var config = Path.Combine(LegacyFixture.Root, ".local", "original-data.txt");
        if (source is null && File.Exists(config)) source = File.ReadAllText(config).Trim();
        if (source is null || !Directory.Exists(source)) Assert.Inconclusive("Set JINYONG_ORIGINAL_DATA to validate original assets.");
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(source!, name));
        return LegacyGameDataImporter.Read(Read, LegacyArchive.Parse(Read("RANGER.IDX"), Read("RANGER.GRP")));
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualGlobalCatalogClassifiesEveryScriptAndValidatesAllReferencesTogether()
    {
        var data = Original.Value;
        Assert.AreEqual(998, data.Programs.Length);
        Assert.AreEqual(20, data.UnavailablePrograms.Count);
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 1018).ToArray(),
            data.Programs.Select(program => program.Id).Concat(data.UnavailablePrograms.Keys).ToArray());
        Assert.AreEqual(320, data.Party!.Roles.Count); Assert.AreEqual(200, data.ItemNames.Count);
        Assert.IsTrue(data.UnavailablePrograms.Values.All(reason => !string.IsNullOrWhiteSpace(reason)));
        var catalog = data.ToStoryCatalog(70, 691, 0, 6890, Direction.Right);
        Assert.AreEqual(691, catalog.InitialProgramId); Assert.AreEqual(998, catalog.Programs.Count);
        Assert.IsNull(catalog.World);
        var referenced = data.Programs.SelectMany(program => program.Commands).SelectMany(command => command.Operation switch
        {
            EventOperation.Dialogue => new[] { command.Arguments[0] },
            EventOperation.RandomDialogue => Enumerable.Range(command.Arguments[0], command.Arguments[1]),
            _ => Enumerable.Empty<int>()
        }).Distinct().ToArray();
        CollectionAssert.AreEquivalent(referenced, data.Dialogues.Select(dialogue => dialogue.Id).ToArray());
        foreach (var id in new[] { 648, 650, 701, 702, 749, 750, 751, 752, 799, 800 })
            Assert.IsTrue(catalog.Programs.ContainsKey(id), $"Computed global KDEF ID {id} was omitted.");
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void ActualPreviouslyUnprofiledCounterRunsSuccessiveComputedScriptsInTheSameVm()
    {
        var data = Original.Value;
        var points = Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray();
        points[0][3] = 701; points[0][9] = points[0][10] = 20;
        var scene = MovementTests.Scene(events: points.Select(words => new SceneEventDefinition(words)).ToArray());
        var session = new GameSession(scene, data.ToStoryCatalog(1));
        ((IEventWorld)session).AddItem(186, 2);
        Assert.IsTrue(session.Events!.TryStart(701, 0, new(20, 20), usedItem: 186), session.Events.Error);
        EventTests.Drain(session);
        Assert.AreEqual((short)702, session.Scene.Events[0][3]); Assert.AreEqual(1, session.Inventory[186]);
        Assert.IsTrue(session.Events.TryStart(session.Scene.Events[0][3], 0, new(20, 20), usedItem: 186), session.Events.Error);
        EventTests.Drain(session);
        Assert.AreEqual((short)703, session.Scene.Events[0][3]); Assert.IsFalse(session.Inventory.ContainsKey(186));
        Assert.AreEqual(2, session.Events.CompletedCount); Assert.IsNull(session.Events.Error);
    }

    [TestMethod]
    [TestCategory("OriginalAssets")]
    public void GlobalDataJsonRoundTripContainsNoSceneOrWorldAndStillValidates()
    {
        var json = JsonSerializer.Serialize(Original.Value, ContentJson.Options);
        using var parsed = JsonDocument.Parse(json);
        Assert.IsFalse(parsed.RootElement.TryGetProperty("scene_id", out _));
        Assert.IsFalse(parsed.RootElement.TryGetProperty("world", out _));
        Assert.IsFalse(parsed.RootElement.TryGetProperty("initial_program_id", out _));
        var restored = JsonSerializer.Deserialize<GameDataDocument>(json, ContentJson.Options)!;
        var catalog = restored.ToStoryCatalog(70, 691, 0, 6890, Direction.Right);
        Assert.AreEqual(Original.Value.Programs.Length, catalog.Programs.Count);
        Assert.AreEqual(Original.Value.Dialogues.Length, catalog.Dialogues.Count);
        Assert.AreEqual(Original.Value.Party!.Roles[0].Name, catalog.Party!.Roles[0].Name);
    }

    [TestMethod]
    public void InvalidProgramReferencesBecomeUnavailableAndUnusedInvalidTalkIsNeverDecoded()
    {
        // Independently authored format fixture; not original game content.
        var (source, _) = LegacyFixture.Create();
        void Pair(string name, params byte[][] entries)
        {
            using var bytes = new MemoryStream(); var ends = new List<uint>();
            foreach (var entry in entries) { bytes.Write(entry); ends.Add((uint)bytes.Length); }
            File.WriteAllBytes(Path.Combine(source, name + ".IDX"), LegacyFormatTests.Index(ends.ToArray()));
            File.WriteAllBytes(Path.Combine(source, name + ".GRP"), bytes.ToArray());
        }
        byte[] Script(params short[] words) => words.SelectMany(BitConverter.GetBytes).ToArray();
        Pair("KDEF", Script(-1), Script(23, 999, 1, -1), Script(1, 9999, 0, 0, -1),
            Script(3, -2, 0, -2, -2, 99, -1, -1, -2, -2, -2, -2, -2, -2, -1),
            Script(8, 1, -1),
            Script(3, -2, 0, -2, -2, 4, -1, -1, -2, -2, -2, -2, -2, -2, -1), Script(1, 0, 0, 0, -1));
        Pair("TALK", new byte[] { 0 }, new byte[] { 0x7E });
        var reads = new Dictionary<string, int>();
        byte[] Read(string name)
        {
            reads[name] = reads.GetValueOrDefault(name) + 1;
            return File.ReadAllBytes(Path.Combine(source, name));
        }
        var data = LegacyGameDataImporter.Read(Read, LegacyArchive.Parse(Read("RANGER.IDX"), Read("RANGER.GRP")));
        CollectionAssert.AreEquivalent(new[] { 0, 5, 6 }, data.Programs.Select(program => program.Id).ToArray());
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }, data.UnavailablePrograms.Keys.ToArray());
        StringAssert.Contains(data.UnavailablePrograms[1], "Unknown role");
        StringAssert.Contains(data.UnavailablePrograms[2], "TALK 9999");
        StringAssert.Contains(data.UnavailablePrograms[3], "Unknown replacement");
        StringAssert.Contains(data.UnavailablePrograms[4], "opcode 8");
        CollectionAssert.AreEqual(new[] { 0 }, data.Dialogues.Select(dialogue => dialogue.Id).ToArray());
        Assert.IsTrue(reads.Values.All(count => count == 1));
        _ = data.ToStoryCatalog(70);
    }

    [TestMethod]
    public void ExtendedDialectIsRejectedBeforeReadingAnyGlobalFiles()
    {
        var calls = 0;
        var ranger = LegacyArchive.Parse(LegacyFormatTests.Index(0), []);
        Assert.ThrowsExactly<NotSupportedException>(() => LegacyGameDataImporter.Read(_ => { calls++; return []; },
            ranger, LegacyScriptDialect.Extended50));
        Assert.AreEqual(0, calls);
    }
}
