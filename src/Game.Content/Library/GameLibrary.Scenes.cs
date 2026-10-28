using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;

namespace JinYong.Content.Library;

public sealed partial class GameLibrary
{
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
}
