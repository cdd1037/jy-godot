using System.Buffers.Binary;
using System.Text;
using JinYong.Core.Maps;

namespace JinYong.Importing.Legacy;

/// <summary>
/// Imports world scene data only, without reading/writing textures or modifying source resources.
/// OpenLegend references: model/game_snapshot.hpp scene_metadata_word, SceneArchives;
/// model/game_snapshot.cpp Ranger/SceneArchives parsing. Archive indices are scene IDs.
/// </summary>
public static class LegacyWorldImporter
{
    public static WorldDefinition Read(LegacyArchive ranger, LegacyArchive allsin, LegacyArchive alldef)
    {
        if (ranger.Count != 6) throw new InvalidDataException("RANGER.IDX must contain six sections.");
        var metadata = ranger.Entry(3);
        if (metadata.Length == 0 || metadata.Length % 52 != 0)
            throw new InvalidDataException("RANGER scene metadata must contain 52-byte records.");
        var metadataCount = metadata.Length / 52;
        var count = allsin.Count;
        // DOS: 84 metadata records and 100 map/event slots. Preserve the extra slots,
        // explicitly marking that they have no metadata rather than inventing entrances.
        if (allsin.Count != alldef.Count || metadataCount > count)
            throw new InvalidDataException("ALLSIN and ALLDEF counts must match and cover all RANGER scene metadata.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var scenes = new WorldSceneDefinition[count];
        for (var sceneId = 0; sceneId < count; sceneId++)
        {
            var hasMetadata = sceneId < metadataCount;
            var record = hasMetadata ? metadata.Slice(sceneId * 52, 52) : new byte[52];
            var nameBytes = record.Slice(2, 10);
            var end = nameBytes.IndexOf((byte)0);
            var name = hasMetadata ? encoding.GetString(end >= 0 ? nameBytes[..end] : nameBytes).TrimEnd() : $"Scene {sceneId}";
            var map = allsin.Entry(sceneId);
            var events = alldef.Entry(sceneId);
            if (map.Length != SceneDefinition.LayerCount * SceneDefinition.TileCount * 2)
                throw new InvalidDataException($"Scene {sceneId} map must contain 49152 bytes.");
            if (events.Length != 200 * 11 * 2)
                throw new InvalidDataException($"Scene {sceneId} events must contain 4400 bytes.");
            var eventWords = new short[200][];
            for (var index = 0; index < eventWords.Length; index++) eventWords[index] = Words(events.Slice(index * 22, 22));
            try { scenes[sceneId] = new WorldSceneDefinition(sceneId, name, Words(record), Words(map), eventWords, hasMetadata); }
            catch (ArgumentException error) { throw new InvalidDataException($"Invalid scene {sceneId} world data.", error); }
        }
        return new WorldDefinition(scenes);
    }

    private static short[] Words(ReadOnlySpan<byte> bytes)
    {
        var words = new short[bytes.Length / 2];
        for (var index = 0; index < words.Length; index++) words[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(index * 2)..]);
        return words;
    }
}
