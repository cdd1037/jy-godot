using JinYong.Importing.Imaging;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using JinYong.Content.Packages;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Importing.Legacy;

internal sealed record ImportOptions(string SourceDirectory, string OutputDirectory, int SceneId = 70, GridPosition? Spawn = null,
    bool IncludeStory = false);
internal sealed record ImportReport(string PackageDirectory, string SceneName, int SpriteCount, int SceneId);

/// <summary>
/// Test-only format-2 map fixture writer for the format-1/2 compatibility reader.
/// This retains historical type names to keep regression tests stable. Production
/// imports use LegacyGameImporter and the shared format-3 game library exclusively.
/// No story closure, copied MOD package, or script dependency profile is supported.
/// </summary>
internal sealed class LegacySceneImporter
{
    public ImportReport Import(ImportOptions options, IProgress<string>? progress = null)
    {
        if (options.IncludeStory) throw new NotSupportedException("Legacy scene fixtures contain maps only; use the game-library importer for story data.");
        if (options.SceneId is < 0 or > 32766) throw new ArgumentOutOfRangeException(nameof(options.SceneId));
        var source = Path.GetFullPath(options.SourceDirectory);
        var output = Path.GetFullPath(options.OutputDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Original data directory does not exist: {source}");
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Import output must be outside the original data directory.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException($"Output already exists: {output}");
        var files = Directory.GetFiles(source).ToDictionary(path => Path.GetFileName(path), path => path, StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, string>();
        byte[] Read(string name)
        {
            if (!files.TryGetValue(name, out var path)) throw new FileNotFoundException($"Missing original resource: {name}", name);
            var bytes = File.ReadAllBytes(path);
            hashes[name] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return bytes;
        }

        progress?.Report("读取原版地图、事件与场景资料…");
        var ranger = LegacyArchive.Parse(Read("RANGER.IDX"), Read("RANGER.GRP"));
        if (ranger.Count != 6) throw new InvalidDataException("RANGER.IDX must contain six data sections.");
        var metadataBytes = ranger.Entry(3).ToArray();
        if (metadataBytes.Length % 52 != 0 || options.SceneId >= metadataBytes.Length / 52)
            throw new InvalidDataException($"Scene {options.SceneId} is outside RANGER metadata.");
        var metadata = metadataBytes.AsSpan(options.SceneId * 52, 52);
        var maps = LegacyArchive.Parse(Read("ALLSIN.IDX"), Read("ALLSIN.GRP"));
        var events = LegacyArchive.Parse(Read("ALLDEF.IDX"), Read("ALLDEF.GRP"));
        if (maps.Count != events.Count) throw new InvalidDataException("ALLSIN and ALLDEF scene counts differ.");
        var layerBytes = maps.Entry(options.SceneId).ToArray();
        if (layerBytes.Length != SceneDefinition.LayerCount * SceneDefinition.TileCount * 2)
            throw new InvalidDataException("Scene map record must contain 49152 bytes.");
        var layers = new short[layerBytes.Length / 2];
        for (var i = 0; i < layers.Length; i++) layers[i] = BinaryPrimitives.ReadInt16LittleEndian(layerBytes.AsSpan(i * 2));
        var eventBytes = events.Entry(options.SceneId);
        if (eventBytes.Length != 4400) throw new InvalidDataException("Scene event record must contain 4400 bytes.");
        var eventWords = new short[200][];
        for (var i = 0; i < eventWords.Length; i++)
        {
            eventWords[i] = new short[11];
            for (var j = 0; j < 11; j++) eventWords[i][j] = BinaryPrimitives.ReadInt16LittleEndian(eventBytes.Slice(i * 22 + j * 2));
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var nameBytes = metadata.Slice(2, 10);
        var zero = nameBytes.IndexOf((byte)0);
        var name = Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            .GetString(zero >= 0 ? nameBytes[..zero] : nameBytes).TrimEnd();
        var spawn = options.Spawn ?? (options.SceneId == 70 ? new GridPosition(19, 20) : new GridPosition(
            BinaryPrimitives.ReadInt16LittleEndian(metadata[28..]), BinaryPrimitives.ReadInt16LittleEndian(metadata[30..])));
        var id = $"base:scene/{options.SceneId:D3}";
        var scene = new SceneDefinition(id, name, spawn, layers, eventWords.Select(words => new SceneEventDefinition(words)));
        _ = new GameSession(scene); // Validate spawn before writing any output.

        progress?.Report("解码场景贴图与人物动画…");
        var sprites = LegacyArchive.Parse(Read($"SDX{options.SceneId:D3}"), Read($"SMP{options.SceneId:D3}"), sentinel: true);
        var rawPalette = Read("MMAP.COL");
        var palette = LegacySpriteDecoder.ReadPalette(rawPalette);
        var required = new SortedSet<int> { 0 };
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var position = new GridPosition(x, y);
            required.Add(scene.At(MapLayer.Ground, position));
            var building = scene.At(MapLayer.Building, position);
            if (building != 0 && building != 15000) required.Add(building);
            var decoration = scene.At(MapLayer.Decoration, position);
            if (decoration != 0) required.Add(decoration);
            if (scene.EventAt(position) is { Picture: > 0 } point) required.Add(point.Picture);
        }
        foreach (var first in new[] { 5002, 5016, 5030, 5044 })
            for (var frame = 0; frame <= 12; frame += 2) required.Add(first + frame);
        var decoded = new Dictionary<int, IndexedSprite>();
        foreach (var spriteId in required)
        {
            if (spriteId < 0 || (spriteId & 1) != 0) throw new InvalidDataException($"Invalid doubled sprite ID {spriteId}.");
            try { decoded.Add(spriteId, LegacySpriteDecoder.Decode(sprites.Entry(spriteId / 2, aliasEmpty: true))); }
            catch (InvalidDataException error) { throw new InvalidDataException($"Sprite {spriteId} in scene {options.SceneId}: {error.Message}", error); }
        }
        foreach (var first in new[] { 5002, 5016, 5030, 5044 })
            if (decoded[first].Width == 0) throw new InvalidDataException($"Player sprite {first} is empty.");

        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("写入标准场景包与贴图图集…");
            var assets = SpriteAtlasWriter.Write(staging, decoded, palette);
            File.WriteAllBytes(Path.Combine(staging, "layers.bin"), layerBytes);
            File.WriteAllBytes(Path.Combine(staging, "palette.rgb6"), rawPalette);
            ContentJson.Write(Path.Combine(staging, "scene.json"), new SceneDocument
            {
                FormatVersion = 2, Id = id, LegacySceneId = options.SceneId, Name = name, SpawnX = spawn.X, SpawnY = spawn.Y,
                Events = eventWords, Sprites = assets, LegacyMetadata = Convert.ToBase64String(metadata)
            });
            var fileHashes = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).ToDictionary(
                path => Path.GetRelativePath(staging, path).Replace('\\', '/'),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            ContentJson.Write(Path.Combine(staging, "manifest.json"), new PackageManifest { FormatVersion = 2, SourceHashes = hashes, FileHashes = fileHashes });
            _ = ScenePackageReader.Load(staging);
            Directory.Move(staging, output);
        }
        catch
        {
            // This path is created above, never derived from source resource names.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
        return new ImportReport(output, name, decoded.Count, options.SceneId);
    }
}
