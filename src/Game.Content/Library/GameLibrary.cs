using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using JinYong.Content.Legacy;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;

namespace JinYong.Content.Library;

/// <summary>Owns shared definitions and file locations. OpenScene creates a view, never a new export.</summary>
public sealed class GameLibrary
{
    private readonly StoryCatalog _definitions;
    private GameLibrary(string directory, GameLibraryManifest manifest, LibraryScene[] scenes, GameDataDocument gameplay,
        WorldDefinition world, SharedSpriteCatalog sprites, LibraryAudio audio, Dictionary<string, SourceResource> sources,
        StoryCatalog definitions, WorldMapDefinition? mainMap)
    {
        Directory = directory; Manifest = manifest; Scenes = Array.AsReadOnly(scenes); GameData = gameplay;
        World = world; Sprites = sprites; Audio = audio;
        Sources = new ReadOnlyDictionary<string, SourceResource>(sources);
        _definitions = definitions;
        MainMap = mainMap;
    }

    public string Directory { get; }
    public GameLibraryManifest Manifest { get; }
    public IReadOnlyList<LibraryScene> Scenes { get; }
    public GameDataDocument GameData { get; }
    public WorldDefinition World { get; }
    public WorldMapDefinition? MainMap { get; }
    public SharedSpriteCatalog Sprites { get; }
    public LibraryAudio Audio { get; }
    public IReadOnlyDictionary<string, SourceResource> Sources { get; }
    public string Resolve(string path) => ScenePackageReader.Resolve(Directory, path);

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
        _ = LegacySpriteDecoder.ReadPalette(File.ReadAllBytes(Tracked(manifest.Palette)));
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
            if (id < 0 || ((bank == "world" || bank.StartsWith("scene/", StringComparison.Ordinal)) && (id & 1) != 0) || !sprites.Assets.ContainsKey(asset))
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
        var sharedCatalog = gameplay.ToStoryCatalog(manifest.DefaultScene, world: world);
        return new(directory, manifest, scenes, gameplay, world, sprites, audio, sources, sharedCatalog, mainMap);
    }

    public ScenePackage OpenScene(int? sceneId = null, GridPosition? spawn = null, StoryDocument? overlay = null, bool validateSpawn = true)
    {
        var id = sceneId ?? Manifest.DefaultScene;
        var entry = Scenes.FirstOrDefault(scene => scene.SceneId == id) ?? throw new ArgumentException($"Unknown scene {id}.");
        if (entry.SpriteBank is null || !Sprites.Banks.ContainsKey(entry.SpriteBank))
            throw new InvalidOperationException($"Scene {id} has map data but no original sprite bank.");
        var position = spawn ?? new GridPosition(entry.SpawnX, entry.SpawnY);
        var scene = World.Scene(id).ToSceneDefinition(position);
        if (validateSpawn) _ = new JinYong.Core.Session.GameSession(scene);
        SpriteAsset[] Bank(string bank) => Sprites.Banks.TryGetValue(bank, out var entries)
            ? entries.OrderBy(pair => pair.Key).Select(pair => Sprites.Assets[pair.Value] with { LegacyId = pair.Key }).ToArray() : [];
        var frames = Bank(entry.SpriteBank);
        var available = frames.ToDictionary(frame => frame.LegacyId);
        void RequireFrame(int sprite)
        {
            if (!available.ContainsKey(sprite)) throw new InvalidDataException($"Scene {id} references missing sprite {sprite}.");
        }
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
        {
            var cell = new GridPosition(x, y);
            RequireFrame(scene.At(MapLayer.Ground, cell));
            if (scene.At(MapLayer.Building, cell) is not (0 or 15000)) RequireFrame(scene.At(MapLayer.Building, cell));
            if (scene.At(MapLayer.Decoration, cell) != 0) RequireFrame(scene.At(MapLayer.Decoration, cell));
            if (scene.EventAt(cell) is { Picture: > 0 } point) RequireFrame(point.Picture);
        }
        foreach (var first in new[] { 5002,5016,5030,5044 })
        {
            for (var offset = 0; offset <= 12; offset += 2) RequireFrame(first + offset);
            if (available[first].Width == 0) throw new InvalidDataException("Player sprite is empty.");
        }
        var data = GameData;
        IEnumerable<EventProgram> programs = _definitions.Programs.Values;
        IEnumerable<DialogueDefinition> dialogues = _definitions.Dialogues.Values;
        int context = scene.At(MapLayer.Event, position);
        int? initial = context >= 0 && scene.Events[context][4] > 0 ? scene.Events[context][4] : null;
        int? picture = id == 70 && initial == 691 ? 6890 : null;
        var facing = id == 70 ? Direction.Right : Direction.Down;
        if (overlay is not null)
        {
            if (overlay.FormatVersion != 1 || overlay.SceneId != id)
                throw new InvalidDataException("Story overlay format or target scene mismatch.");
            data = data with
            {
                ItemNames = data.ItemNames.Concat(overlay.ItemNames).ToDictionary(pair => pair.Key, pair => pair.Value),
                VariableNames = data.VariableNames.Concat(overlay.VariableNames).ToDictionary(pair => pair.Key, pair => pair.Value),
                UnavailablePrograms = data.UnavailablePrograms.Concat(overlay.UnavailablePrograms).ToDictionary(pair => pair.Key, pair => pair.Value)
            };
            programs = programs.Concat(overlay.Programs.Select(program => new EventProgram(program.Id,
                program.Commands.Select(command => new EventCommand(command.Id, command.Operation, command.Arguments,
                    command.SourceOpcode, command.Text, command.Choices)))));
            dialogues = dialogues.Concat(overlay.Dialogues.Select(dialogue => new DialogueDefinition(dialogue.Id, dialogue.Pages)));
            initial = overlay.InitialProgramId; context = overlay.InitialEventIndex;
            picture = overlay.InitialPicture; facing = overlay.InitialFacing;
        }
        var story = data.Programs.Length == 0 && data.Party is null && overlay is null ? null :
            new StoryCatalog(id, initial, context, picture, facing, programs, dialogues, data.ItemNames, data.InitialInventory,
                data.UnavailablePrograms, data.VariableNames, data.Party, World, data.InitialItemIds);
        var document = new SceneDocument
        {
            FormatVersion = 3, Id = scene.Id, LegacySceneId = id, Name = scene.Name,
            SpawnX = position.X, SpawnY = position.Y, Layers = Manifest.WorldLayers, Palette = Manifest.Palette,
            LegacyMetadata = Convert.ToBase64String(entry.Metadata.SelectMany(BitConverter.GetBytes).ToArray()),
            Events = World.Scene(id).Events.ToArray(), Sprites = frames, Portraits = Bank("portrait"),
            PortraitNames = data.PortraitNames, Sounds = Audio.Effects, Story = story is null ? null : Manifest.GameData
        };
        return new ScenePackage(Directory, new PackageManifest
        {
            FormatVersion = 3, Id = Manifest.Id, EngineApi = Manifest.EngineApi, Version = Manifest.Version,
            Scene = Manifest.SceneIndex, FileHashes = Manifest.FileHashes, SourceHashes = Manifest.SourceHashes
        }, document, scene, story) { Library = this };
    }

    public ScenePackage OpenWorldView(JinYong.Core.Session.GameSession session)
    {
        if (MainMap is null || !Sprites.Banks.TryGetValue("world", out var mapping))
            throw new InvalidOperationException("World travel resources are missing; reimport the complete library.");
        var document = new SceneDocument
        {
            FormatVersion = 3, Id = "base:world", LegacySceneId = -1, Name = "江湖世界",
            Layers = Manifest.MainMap!, Palette = Manifest.Palette,
            Sprites = mapping.OrderBy(pair => pair.Key).Select(pair => Sprites.Assets[pair.Value] with { LegacyId = pair.Key }).ToArray(),
            Portraits = Sprites.Banks["portrait"].OrderBy(pair => pair.Key).Select(pair => Sprites.Assets[pair.Value] with { LegacyId = pair.Key }).ToArray(),
            PortraitNames = GameData.PortraitNames, Sounds = Audio.Effects, Story = Manifest.GameData
        };
        return new(Directory, new PackageManifest
        {
            FormatVersion = 3, Id = Manifest.Id, EngineApi = Manifest.EngineApi, Version = Manifest.Version,
            Scene = Manifest.SceneIndex, FileHashes = Manifest.FileHashes, SourceHashes = Manifest.SourceHashes
        }, document, session.Definition, session.Story) { Library = this };
    }
}
