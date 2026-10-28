using System.Buffers.Binary;
using System.Security.Cryptography;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using JinYong.Core.Events;
using JinYong.Content.Library;
using System.Text.Json;

namespace JinYong.Content.Packages;

public sealed record ScenePackage(string Directory, PackageManifest Manifest, SceneDocument Document, SceneDefinition Scene, StoryCatalog? Story = null)
{
    public string Resolve(string relativePath) => ScenePackageReader.Resolve(Directory, relativePath);
    public GameLibrary? Library { get; init; }
}

public static class ScenePackageReader
{
    public static ScenePackage Load(string directory, int? sceneId = null, GridPosition? spawn = null, StoryDocument? storyOverlay = null)
    {
        directory = Path.GetFullPath(directory);
        using (var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json"))))
            if (metadata.RootElement.GetProperty("format_version").GetInt32() == 3)
                return GameLibrary.Load(directory).OpenScene(sceneId, spawn, storyOverlay);
        if (spawn.HasValue || storyOverlay is not null)
            throw new InvalidOperationException("Spawn overrides and overlays require a unified game library. Reimport the original resources.");
        // A legacy package contains exactly one scene. A host's current preview
        // selector must not prevent opening that package for read-only migration.
        var manifest = ContentJson.Read<PackageManifest>(Path.Combine(directory, "manifest.json"));
        if (manifest.FormatVersion is not 1 and not 2 || manifest.EngineApi != "1" || manifest.Id != "base")
            throw new InvalidDataException("Unsupported content package version or ID.");
        foreach (var (path, hash) in manifest.FileHashes)
        {
            var bytes = File.ReadAllBytes(Resolve(directory, path));
            if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Content checksum mismatch: {path}");
        }
        void RequireTracked(string path)
        {
            if (!manifest.FileHashes.ContainsKey(path)) throw new InvalidDataException($"Untracked content file: {path}");
        }
        RequireTracked(manifest.Scene);
        var document = ContentJson.Read<SceneDocument>(Resolve(directory, manifest.Scene));
        if (document.FormatVersion is not 1 and not 2 || document.Id != $"base:scene/{document.LegacySceneId:D3}")
            throw new InvalidDataException("Unsupported scene format or scene ID.");
        RequireTracked(document.Layers);
        RequireTracked(document.Palette);
        _ = Legacy.LegacySpriteDecoder.ReadPalette(File.ReadAllBytes(Resolve(directory, document.Palette)));
        var bytesLayers = File.ReadAllBytes(Resolve(directory, document.Layers));
        if (bytesLayers.Length != SceneDefinition.LayerCount * SceneDefinition.TileCount * 2)
            throw new InvalidDataException("Invalid scene layer byte count.");
        var layers = new short[bytesLayers.Length / 2];
        for (var i = 0; i < layers.Length; i++) layers[i] = BinaryPrimitives.ReadInt16LittleEndian(bytesLayers.AsSpan(i * 2));
        var scene = new SceneDefinition(document.Id, document.Name, new(document.SpawnX, document.SpawnY), layers,
            document.Events.Select(words => new SceneEventDefinition(words)));
        _ = new GameSession(scene);
        var ids = new HashSet<int>();
        foreach (var sprite in document.Sprites)
        {
            if (!ids.Add(sprite.LegacyId) || sprite.LegacyId < 0 || (sprite.LegacyId & 1) != 0)
                throw new InvalidDataException($"Duplicate or invalid sprite ID {sprite.LegacyId}.");
            if (sprite.Width < 0 || sprite.Height < 0 || sprite.X < 0 || sprite.Y < 0 ||
                sprite.Width > 1024 || sprite.Height > 1024 || sprite.X + sprite.Width > 1024)
                throw new InvalidDataException($"Invalid atlas rectangle for sprite {sprite.LegacyId}.");
            if (sprite.Width > 0 && sprite.Height > 0) RequireTracked(sprite.Atlas);
            else if (sprite.Width != 0 || sprite.Height != 0 || sprite.Atlas != "")
                throw new InvalidDataException($"Invalid empty sprite {sprite.LegacyId}.");
        }
        void RequireSprite(int id)
        {
            if (!ids.Contains(id)) throw new InvalidDataException($"Missing referenced sprite {id}.");
        }
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var point = new GridPosition(x, y);
            RequireSprite(scene.At(MapLayer.Ground, point));
            var building = scene.At(MapLayer.Building, point);
            if (building is not 0 and not 15000) RequireSprite(building);
            var decoration = scene.At(MapLayer.Decoration, point);
            if (decoration != 0) RequireSprite(decoration);
            if (scene.EventAt(point) is { Picture: > 0 } pointEvent) RequireSprite(pointEvent.Picture);
        }
        foreach (var first in new[] { 5002, 5016, 5030, 5044 })
        {
            for (var frame = 0; frame <= 12; frame += 2) RequireSprite(first + frame);
            if (!document.Sprites.Any(sprite => sprite.LegacyId == first && sprite.Width > 0))
                throw new InvalidDataException($"Missing player sprite {first}.");
        }
        StoryCatalog? story = null;
        if (document.Story is { } storyPath)
        {
            RequireTracked(storyPath);
            var storyDocument = ContentJson.Read<StoryDocument>(Resolve(directory, storyPath));
            if (storyDocument.FormatVersion != 1 || storyDocument.SceneId != document.LegacySceneId)
                throw new InvalidDataException("Story format or scene ID mismatch.");
            story = storyDocument.ToCatalog();
            foreach (var portrait in document.Portraits)
                if (portrait.Width > 0) RequireTracked(portrait.Atlas);
            foreach (var sound in document.Sounds.Values) RequireTracked(sound);
            foreach (var command in story.Programs.Values.SelectMany(program => program.Commands))
            {
                var arguments = command.Arguments;
                switch (command.Operation)
                {
                    case EventOperation.Dialogue:
                        if (!story.Dialogues.ContainsKey(arguments[0]) || !document.Portraits.Any(portrait => portrait.LegacyId == arguments[1]))
                            throw new InvalidDataException("Missing dialogue text or portrait.");
                        break;
                    case EventOperation.RandomDialogue:
                        for (var i = arguments[0]; i < arguments[0] + arguments[1]; i++)
                            if (!story.Dialogues.ContainsKey(i)) throw new InvalidDataException($"Missing random dialogue {i}.");
                        if (!document.Portraits.Any(portrait => portrait.LegacyId == arguments[2])) throw new InvalidDataException("Missing random dialogue portrait.");
                        break;
                    case EventOperation.Animate:
                        for (var i = arguments[1]; i <= arguments[2]; i += 2) RequireSprite(i);
                        break;
                    case EventOperation.ChangeEvent:
                        if (arguments[0] == -2 || arguments[0] == story.SceneId)
                            foreach (var i in arguments.Skip(7).Take(3).Where(i => i > 0)) RequireSprite(i);
                        break;
                    case EventOperation.Sound:
                        if (!document.Sounds.ContainsKey(arguments[0])) throw new InvalidDataException("Missing scene sound.");
                        break;
                    case EventOperation.AddItem: case EventOperation.AdjustItem: case EventOperation.BranchItem: case EventOperation.BranchUsedItem:
                        if (!story.ItemNames.ContainsKey(arguments[0])) throw new InvalidDataException("Unknown reward item.");
                        break;
                    case EventOperation.SetTile:
                        if ((arguments[0] == -2 || arguments[0] == story.SceneId) && arguments[1] is >= 0 and <= 2 && arguments[4] >= 0 && arguments[4] != 15000) RequireSprite(arguments[4]);
                        break;
                    case EventOperation.ReplaceTiles:
                        if ((arguments[0] == -2 || arguments[0] == story.SceneId) && arguments[1] is >= 0 and <= 2 && arguments[3] >= 0 && arguments[3] != 15000) RequireSprite(arguments[3]);
                        break;
                }
            }
            if (story.InitialPicture.HasValue) RequireSprite(story.InitialPicture.Value);
        }
        return new ScenePackage(directory, manifest, document, scene, story);
    }

    public static string Resolve(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Content path must be relative.");
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Content path escapes package: {relativePath}");
        return full;
    }
}
