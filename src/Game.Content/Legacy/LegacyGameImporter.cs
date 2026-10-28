using System.Buffers.Binary;
using System.Security.Cryptography;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Maps;

namespace JinYong.Content.Legacy;

/// <summary>Imports a complete game library once. No scene owns copies of shared data or assets.</summary>
public sealed class LegacyGameImporter
{
    public LibraryImportReport Import(LibraryImportOptions options, IProgress<string>? progress = null)
    {
        if (options.IncludeStory && options.ScriptDialect != LegacyScriptDialect.DosOriginal)
            throw new NotSupportedException("Only DOS original scripts are supported; extended 50 requires an explicit converter.");
        var source = Path.GetFullPath(options.SourceDirectory);
        var output = Path.GetFullPath(options.OutputDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Original data directory does not exist: {source}");
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(
            source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Library output must be outside the original resource directory.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException($"Output already exists: {output}");
        var files = Directory.GetFiles(source).ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] Read(string name)
        {
            if (!files.TryGetValue(name, out var path)) throw new FileNotFoundException($"Missing original resource: {name}", name);
            var bytes = File.ReadAllBytes(path);
            hashes[name.ToUpperInvariant()] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return bytes;
        }
        progress?.Report("读取全局地图、人物与剧情数据…");
        var ranger = LegacyArchive.Parse(Read("RANGER.IDX"), Read("RANGER.GRP"));
        var maps = LegacyArchive.Parse(Read("ALLSIN.IDX"), Read("ALLSIN.GRP"));
        var events = LegacyArchive.Parse(Read("ALLDEF.IDX"), Read("ALLDEF.GRP"));
        var world = LegacyWorldImporter.Read(ranger, maps, events);
        var gameplay = options.IncludeStory ? LegacyGameDataImporter.Read(Read, ranger, options.ScriptDialect) : new GameDataDocument();
        var hasMainMap = LegacyWorldMapImporter.LayerFiles.Any(files.ContainsKey);
        var mainMap = hasMainMap ? LegacyWorldMapImporter.Read(Read) : null;
        if (mainMap is not null) gameplay = gameplay with { TravelStart = LegacyWorldMapImporter.ReadState(ranger.Entry(0)) };
        var rawPalette = Read("MMAP.COL");
        var palette = LegacySpriteDecoder.ReadPalette(rawPalette);
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "data"));
            Directory.CreateDirectory(Path.Combine(staging, "assets"));
            var sprites = LegacyAssetImporter.Import(Read, files.Keys.ToArray(), staging, palette,
                world.Scenes.Count(scene => scene.HasLegacyMetadata), progress);
            progress?.Report("聚合原始资源与全局索引…");
            var sources = LegacySourceVault.Import(files.Keys, Read, staging);
            var audio = new LibraryAudio
            {
                Effects = Audio("E", ".WAV"), Attacks = Audio("ATK", ".WAV"), Music = Audio("GAME", ".XMI")
            };
            Dictionary<int, string> Audio(string prefix, string suffix) => sources.Where(pair =>
                    pair.Key.StartsWith(prefix, StringComparison.Ordinal) && pair.Key.EndsWith(suffix, StringComparison.Ordinal) &&
                    int.TryParse(pair.Key.AsSpan(prefix.Length, pair.Key.Length - prefix.Length - suffix.Length), out _))
                .ToDictionary(pair => int.Parse(pair.Key.AsSpan(prefix.Length, pair.Key.Length - prefix.Length - suffix.Length)), pair => pair.Value.Storage);
            using (var layers = File.Create(Path.Combine(staging, "data/world.layers.bin")))
            using (var eventData = File.Create(Path.Combine(staging, "data/world.events.bin")))
                for (var scene = 0; scene < maps.Count; scene++) { layers.Write(maps.Entry(scene)); eventData.Write(events.Entry(scene)); }
            var index = world.Scenes.Select(scene => new LibraryScene
            {
                SceneId = scene.SceneId, Name = scene.Name, HasLegacyMetadata = scene.HasLegacyMetadata,
                Metadata = scene.Metadata.ToArray(),
                SpawnX = scene.SceneId == 70 ? 19 : scene.Metadata[14],
                SpawnY = scene.SceneId == 70 ? 20 : scene.Metadata[15],
                SpriteBank = scene.HasLegacyMetadata ? $"scene/{scene.SceneId:D3}" : null
            }).ToArray();
            ContentJson.Write(Path.Combine(staging, "data/scene-index.json"), index);
            ContentJson.Write(Path.Combine(staging, "data/gameplay.json"), gameplay);
            ContentJson.Write(Path.Combine(staging, "data/sprites.json"), sprites);
            ContentJson.Write(Path.Combine(staging, "data/audio.json"), audio);
            ContentJson.Write(Path.Combine(staging, "data/source-index.json"), sources);
            File.WriteAllBytes(Path.Combine(staging, "assets/palette.rgb6"), rawPalette);
            if (mainMap is not null)
            {
                using var mainLayers = File.Create(Path.Combine(staging, "data/main-map.bin"));
                foreach (var name in LegacyWorldMapImporter.LayerFiles) mainLayers.Write(Read(name));
            }
            var fileHashes = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).ToDictionary(
                path => Path.GetRelativePath(staging, path).Replace('\\', '/'),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
            var manifest = new GameLibraryManifest
            {
                DefaultScene = index.Any(scene => scene.SceneId == 70) ? 70 : index[0].SceneId,
                MainMap = mainMap is null ? null : "data/main-map.bin",
                SourceHashes = hashes, FileHashes = fileHashes
            };
            ContentJson.Write(Path.Combine(staging, "manifest.json"), manifest);
            _ = GameLibrary.Load(staging);
            Directory.Move(staging, output);
            return new(output, index.Length, sprites.Banks.Count, sprites.Assets.Count, gameplay.Programs.Length,
                gameplay.UnavailablePrograms.Count, sources.Count);
        }
        catch
        {
            // Only our own unique sibling staging directory can be removed.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }
}
