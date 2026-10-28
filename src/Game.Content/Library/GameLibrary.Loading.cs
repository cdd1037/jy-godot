using System.Buffers.Binary;
using System.Security.Cryptography;
using JinYong.Content.Imaging;
using JinYong.Content.Packages;
using JinYong.Core.Maps;
using JinYong.Core.Battles;

namespace JinYong.Content.Library;

public sealed partial class GameLibrary
{
    public static GameLibrary Load(string directory)
    {
        directory = Path.GetFullPath(directory);
        var manifest = ContentJson.Read<GameLibraryManifest>(Path.Combine(directory, "manifest.json"));
        if (manifest.FormatVersion != 3 || manifest.EngineApi != "1" || manifest.Id != "base")
            throw new InvalidDataException("Unsupported game library format, engine API or ID.");
        foreach (var (path, expected) in manifest.FileHashes)
        {
            using var stream = File.OpenRead(ScenePackageReader.Resolve(directory, path));
            if (!Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Library checksum mismatch: {path}");
        }
        string Tracked(string path)
        {
            if (!manifest.FileHashes.ContainsKey(path)) throw new InvalidDataException($"Untracked library file: {path}");
            return ScenePackageReader.Resolve(directory, path);
        }
        var scenes = ContentJson.Read<LibraryScene[]>(Tracked(manifest.SceneIndex));
        if (scenes.Length == 0 || scenes.Select(scene => scene.SceneId).Distinct().Count() != scenes.Length ||
            scenes.Where((scene, index) => scene.SceneId != index || scene.Metadata.Length != 26).Any() ||
            !scenes.Any(scene => scene.SceneId == manifest.DefaultScene))
            throw new InvalidDataException("Scene index requires unique contiguous IDs, 26 metadata words and a valid default scene.");
        var layers = File.ReadAllBytes(Tracked(manifest.WorldLayers));
        var eventData = File.ReadAllBytes(Tracked(manifest.WorldEvents));
        var layerSize = SceneDefinition.LayerCount * SceneDefinition.TileCount * 2;
        if (layers.LongLength != (long)scenes.Length * layerSize || eventData.LongLength != (long)scenes.Length * 4400)
            throw new InvalidDataException("World binary lengths do not match the scene index.");
        var definitions = new List<WorldSceneDefinition>();
        foreach (var scene in scenes)
        {
            var words = new short[layerSize / 2];
            for (var i = 0; i < words.Length; i++)
                words[i] = BinaryPrimitives.ReadInt16LittleEndian(layers.AsSpan(scene.SceneId * layerSize + i * 2));
            var events = Enumerable.Range(0, 200).Select(_ => new short[11]).ToArray();
            for (var point = 0; point < 200; point++)
            for (var field = 0; field < 11; field++)
                events[point][field] = BinaryPrimitives.ReadInt16LittleEndian(eventData.AsSpan(scene.SceneId * 4400 + point * 22 + field * 2));
            definitions.Add(new(scene.SceneId, scene.Name, scene.Metadata, words, events, scene.HasLegacyMetadata));
        }
        var world = new WorldDefinition(definitions);
        _ = VgaPalette.DecodeRgb6(File.ReadAllBytes(Tracked(manifest.Palette)));
        var sprites = ContentJson.Read<SharedSpriteCatalog>(Tracked(manifest.Sprites));
        var bounds = new Dictionary<string, (int Width, int Height)>();
        Span<byte> header = stackalloc byte[24];
        foreach (var (key, sprite) in sprites.Assets)
        {
            if (string.IsNullOrWhiteSpace(key) || sprite.Width < 0 || sprite.Height < 0 || sprite.X < 0 || sprite.Y < 0)
                throw new InvalidDataException("Invalid shared sprite geometry.");
            if (sprite.Width == 0 || sprite.Height == 0)
            {
                if (sprite.Width != 0 || sprite.Height != 0 || sprite.Atlas != "")
                    throw new InvalidDataException("Invalid empty shared sprite.");
                continue;
            }
            if (!bounds.TryGetValue(sprite.Atlas, out var size))
            {
                using var png = File.OpenRead(Tracked(sprite.Atlas));
                png.ReadExactly(header);
                if (!header[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) ||
                    !header[12..16].SequenceEqual("IHDR"u8)) throw new InvalidDataException("Invalid sprite atlas PNG.");
                size = (BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
                bounds.Add(sprite.Atlas, size);
            }
            if (size.Width is <= 0 or > 1024 || size.Height is <= 0 or > 1024 ||
                (long)sprite.X + sprite.Width > size.Width || (long)sprite.Y + sprite.Height > size.Height)
                throw new InvalidDataException($"Sprite {key} is outside its shared atlas.");
        }
        foreach (var (bank, entries) in sprites.Banks)
        foreach (var (id, asset) in entries)
            if (id < 0 || ((bank is "world" or "effect" || bank.StartsWith("scene/", StringComparison.Ordinal) ||
                bank.StartsWith("battlefield/", StringComparison.Ordinal) || bank.StartsWith("fight/", StringComparison.Ordinal)) && (id & 1) != 0) || !sprites.Assets.ContainsKey(asset))
                throw new InvalidDataException($"Sprite bank {bank} has an invalid original-ID mapping.");
        foreach (var scene in scenes.Where(scene => scene.SpriteBank is not null))
            if (!sprites.Banks.ContainsKey(scene.SpriteBank!)) throw new InvalidDataException($"Missing sprite bank for scene {scene.SceneId}.");
        var audio = ContentJson.Read<LibraryAudio>(Tracked(manifest.Audio));
        foreach (var path in audio.Effects.Values.Concat(audio.Attacks.Values).Concat(audio.Music.Values)) _ = Tracked(path);
        var sources = ContentJson.Read<Dictionary<string, SourceResource>>(Tracked(manifest.SourceIndex));
        foreach (var (name, resource) in sources)
        {
            var path = Tracked(resource.Storage);
            if (Path.GetFileName(name) != name || resource.Size != new FileInfo(path).Length ||
                !resource.Sha256.Equals(manifest.FileHashes[resource.Storage], StringComparison.OrdinalIgnoreCase) ||
                !resource.Sha256.Equals(manifest.SourceHashes.GetValueOrDefault(name), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Inconsistent original resource index: {name}");
        }
        var gameplay = ContentJson.Read<GameDataDocument>(Tracked(manifest.GameData));
        WorldMapDefinition? mainMap = null;
        if (manifest.MainMap is { } mainPath)
        {
            var mainBytes = File.ReadAllBytes(Tracked(mainPath));
            var perLayer = WorldMapDefinition.CellCount * 2;
            if (mainBytes.Length != perLayer * WorldMapDefinition.LayerCount || !sprites.Banks.ContainsKey("world") || gameplay.TravelStart is null)
                throw new InvalidDataException("World travel requires five main-map layers, the world sprite bank and a starting state.");
            var mainLayers = new short[WorldMapDefinition.LayerCount][];
            for (var layer = 0; layer < mainLayers.Length; layer++)
            {
                mainLayers[layer] = new short[WorldMapDefinition.CellCount];
                for (var cell = 0; cell < mainLayers[layer].Length; cell++)
                    mainLayers[layer][cell] = BinaryPrimitives.ReadInt16LittleEndian(mainBytes.AsSpan(layer * perLayer + cell * 2));
            }
            mainMap = new(mainLayers);
            _ = new WorldTravelState(gameplay.TravelStart);
            var worldFrames = sprites.Banks["world"];
            foreach (var layer in new[] { WorldLayer.Earth, WorldLayer.Surface, WorldLayer.Building })
            foreach (var picture in mainMap.Layer(layer).Distinct())
                if ((picture != 0 || layer == WorldLayer.Earth) && !worldFrames.ContainsKey(picture))
                    throw new InvalidDataException($"World map references missing sprite {picture}.");
            foreach (var first in new[] { 5002, 5016, 5030, 5044, 7430, 7438, 7446, 7454 })
            for (var offset = 0; offset <= (first < 7430 ? 12 : 6); offset += 2)
                if (!worldFrames.TryGetValue(first + offset, out var asset) || sprites.Assets[asset].Width == 0)
                    throw new InvalidDataException($"World actor sprite {first + offset} is missing or empty.");
        }
        BattleCatalog? battles = null;
        if ((manifest.Battles is null) != (manifest.Battlefields is null))
            throw new InvalidDataException("Battle definitions and battlefield data must be provided together.");
        if (manifest.Battles is { } battlePath)
        {
            var document = ContentJson.Read<LibraryBattleDocument>(Tracked(battlePath));
            var bytes = File.ReadAllBytes(Tracked(manifest.Battlefields!));
            const int fieldWords = BattlefieldDefinition.LayerCount * BattlefieldDefinition.TileCount;
            if (document.FormatVersion != 1 || document.BattlefieldIds.Distinct().Count() != document.BattlefieldIds.Length ||
                bytes.LongLength != (long)document.BattlefieldIds.Length * fieldWords * 2)
                throw new InvalidDataException("Invalid battle definition version, field index or battlefield length.");
            var fields = new List<BattlefieldDefinition>();
            for (var field = 0; field < document.BattlefieldIds.Length; field++)
            {
                var words = new short[fieldWords];
                for (var word = 0; word < words.Length; word++)
                    words[word] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan((field * fieldWords + word) * 2));
                fields.Add(new(document.BattlefieldIds[field], words));
            }
            battles = new(document.Battles, fields, document.Magics, document.Items, gameplay.Party, document.Progression);
            foreach (var field in fields)
            {
                if (!sprites.Banks.TryGetValue($"battlefield/{field.Id:D3}", out var frames))
                    throw new InvalidDataException($"Battlefield {field.Id} has no sprite bank.");
                foreach (var picture in field.Layers.Distinct())
                    if (picture != 0 && !frames.ContainsKey(picture))
                        throw new InvalidDataException($"Battlefield {field.Id} references missing sprite {picture}.");
            }
        }
        var sharedCatalog = gameplay.ToStoryCatalog(manifest.DefaultScene, world: world);
        return new(directory, manifest, scenes, gameplay, world, sprites, audio, sources, sharedCatalog, mainMap, battles);
    }
}
