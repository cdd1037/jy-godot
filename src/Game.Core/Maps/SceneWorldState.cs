using System.Collections.ObjectModel;

namespace JinYong.Core.Maps;

/// <summary>
/// Immutable, JSON-serializable source state, independent of Godot and textures.
/// Layout: OpenLegend model/game_snapshot.hpp scene_metadata_word and SceneArchives.
/// SceneId is the archive index; Metadata[0] is preserved rather than used as an index.
/// </summary>
public sealed class WorldSceneDefinition
{
    private readonly ReadOnlyCollection<short> _metadata;
    private readonly ReadOnlyCollection<short> _layers;
    private readonly short[][] _events;

    public WorldSceneDefinition(int sceneId, string name, IReadOnlyList<short> metadata,
        IReadOnlyList<short> layers, IReadOnlyList<short[]> events, bool hasLegacyMetadata = true)
    {
        if (sceneId < 0) throw new ArgumentOutOfRangeException(nameof(sceneId));
        if (metadata.Count != 26) throw new ArgumentException("Scene metadata must contain 26 words.", nameof(metadata));
        if (layers.Count != SceneDefinition.LayerCount * SceneDefinition.TileCount)
            throw new ArgumentException("Scene must contain six 64 by 64 layers.", nameof(layers));
        if (events.Count != 200 || events.Any(words => words.Length != 11))
            throw new ArgumentException("Scene must contain 200 eleven-word events.", nameof(events));
        SceneId = sceneId;
        Name = name;
        HasLegacyMetadata = hasLegacyMetadata;
        _metadata = Array.AsReadOnly(metadata.ToArray());
        _layers = Array.AsReadOnly(layers.ToArray());
        _events = events.Select(words => words.ToArray()).ToArray();
        // Validate references now; unused legacy entrances may legitimately be outside the map.
        _ = ToSceneDefinition();
    }

    public int SceneId { get; }
    public string Name { get; }
    // Original DOS files contain 84 metadata records but 100 map/event records.
    // False marks unused archive slots; their 26 zero words are placeholders, not original metadata.
    public bool HasLegacyMetadata { get; }
    public IReadOnlyList<short> Metadata => _metadata;
    public IReadOnlyList<short> Layers => _layers;
    // Return copies because short[] elements otherwise allow mutation through a read-only list.
    public IReadOnlyList<short[]> Events => Array.AsReadOnly(_events.Select(words => words.ToArray()).ToArray());

    public SceneDefinition ToSceneDefinition(GridPosition? spawn = null)
    {
        var entrance = new GridPosition(_metadata[14], _metadata[15]);
        var position = spawn ?? (HasLegacyMetadata && SceneDefinition.Contains(entrance) ? entrance : new GridPosition(0, 0));
        return new SceneDefinition($"base:scene/{SceneId:D3}", Name, position, _layers,
            _events.Select(words => new SceneEventDefinition(words)));
    }
}

public sealed class WorldDefinition
{
    private readonly ReadOnlyCollection<WorldSceneDefinition> _scenes;
    private readonly Dictionary<int, WorldSceneDefinition> _byId;

    public WorldDefinition(IReadOnlyList<WorldSceneDefinition> scenes)
    {
        _scenes = Array.AsReadOnly(scenes.ToArray());
        _byId = [];
        foreach (var scene in _scenes)
            if (!_byId.TryAdd(scene.SceneId, scene)) throw new ArgumentException($"Duplicate scene {scene.SceneId}.", nameof(scenes));
    }

    public IReadOnlyList<WorldSceneDefinition> Scenes => _scenes;
    public WorldSceneDefinition Scene(int sceneId) => _byId.TryGetValue(sceneId, out var scene)
        ? scene : throw new ArgumentOutOfRangeException(nameof(sceneId), $"Scene {sceneId} is not available.");
    public bool HasScene(int sceneId) => _byId.ContainsKey(sceneId);
}

/// <summary>
/// Mutable world data for one session. The active scene is the exact instance supplied by
/// GameSession; other scenes are copied lazily. This is not a scene renderer or world-map runtime.
/// Opcode semantics reference OpenLegend src/scene/scene.cpp cases 26, 38, 39, 54, 59, 61, 65.
/// </summary>
public sealed class SceneWorldState
{
    private readonly WorldDefinition? _definition;
    private readonly Dictionary<int, SceneRuntimeState> _scenes = [];
    private readonly Dictionary<int, short[]> _metadata = [];
    private static readonly (int Scene, int Event)[] ShopTargets = [(1, 16), (3, 14), (40, 20), (60, 16), (61, 9)];
    private static readonly (int Scene, int Event)[] DepartedCompanionEvents =
    [
        (0, 0), (49, 2), (4, 1), (44, 0), (44, 1), (37, 5), (30, 0), (59, 0),
        (40, 3), (56, 1), (1, 7), (1, 8), (1, 10), (40, 7), (40, 8), (77, 0),
        (54, 0), (62, 3), (62, 4), (60, 2), (60, 15), (52, 1), (61, 0), (61, 8),
        (78, 0), (18, 0), (18, 1), (69, 0), (69, 1), (45, 0), (52, 2), (42, 6),
        (42, 7), (8, 8), (7, 6), (80, 1)
    ];

    public SceneWorldState(int currentSceneId, SceneRuntimeState active, WorldDefinition? definition = null)
    {
        if (currentSceneId < 0) throw new ArgumentOutOfRangeException(nameof(currentSceneId));
        ArgumentNullException.ThrowIfNull(active);
        CurrentSceneId = currentSceneId;
        _definition = definition;
        _scenes.Add(currentSceneId, active);
        if (definition?.HasScene(currentSceneId) != true) _metadata.Add(currentSceneId, new short[26]);
    }

    public int CurrentSceneId { get; }
    public IReadOnlyList<int> SceneIds => (_definition?.Scenes.Select(scene => scene.SceneId) ?? [])
        .Append(CurrentSceneId).Distinct().Order().ToArray();
    public bool HasScene(int sceneId) => _scenes.ContainsKey(Resolve(sceneId)) || _definition?.HasScene(Resolve(sceneId)) == true;
    private int Resolve(int sceneId) => sceneId == -2 ? CurrentSceneId : sceneId;

    public SceneRuntimeState Scene(int sceneId = -2)
    {
        sceneId = Resolve(sceneId);
        if (_scenes.TryGetValue(sceneId, out var scene)) return scene;
        if (_definition is null) throw new ArgumentOutOfRangeException(nameof(sceneId), "The target scene is not available.");
        scene = new SceneRuntimeState(_definition.Scene(sceneId).ToSceneDefinition());
        _scenes.Add(sceneId, scene);
        return scene;
    }

    private short[] Metadata(int sceneId)
    {
        sceneId = Resolve(sceneId);
        if (_metadata.TryGetValue(sceneId, out var words)) return words;
        if (_definition is null) throw new ArgumentOutOfRangeException(nameof(sceneId), "The target scene is not available.");
        words = _definition.Scene(sceneId).Metadata.ToArray();
        _metadata.Add(sceneId, words);
        return words;
    }

    public int MetadataWord(int sceneId, int field)
    {
        if (field is < 0 or >= 26) throw new ArgumentOutOfRangeException(nameof(field));
        return Metadata(sceneId)[field];
    }

    public void SetMetadataWord(int sceneId, int field, int value)
    {
        if (field is < 0 or >= 26) throw new ArgumentOutOfRangeException(nameof(field));
        Metadata(sceneId)[field] = checked((short)value);
    }

    public int EventField(int sceneId, int eventIndex, int field)
    {
        var scene = Scene(sceneId);
        if (eventIndex < 0 || eventIndex >= scene.Events.Count) throw new ArgumentOutOfRangeException(nameof(eventIndex));
        if (field is < 0 or >= 11) throw new ArgumentOutOfRangeException(nameof(field));
        return scene.Events[eventIndex][field];
    }

    public void SetEventField(int sceneId, int eventIndex, int field, int value) => Scene(sceneId).SetEventField(eventIndex, field, value);
    public int Tile(int sceneId, MapLayer layer, GridPosition position) => Scene(sceneId).At(layer, position);
    public void SetTile(int sceneId, MapLayer layer, GridPosition position, int value) => Scene(sceneId).SetTile(layer, position, value);

    public void ValidateChangeEvent(int sceneId, int eventContext, IReadOnlyList<int> arguments, bool validateCurrentPosition = true)
    {
        if (arguments.Count != 13) throw new ArgumentException("An event change requires 13 arguments.", nameof(arguments));
        var target = arguments[0] == -2 ? Resolve(sceneId) : arguments[0];
        Scene(target).ValidateChangeEvent(target, eventContext, arguments, validateCurrentPosition);
    }

    public void ChangeEvent(int sceneId, int eventContext, GridPosition triggerPosition, IReadOnlyList<int> arguments)
    {
        ValidateChangeEvent(sceneId, eventContext, arguments);
        var target = arguments[0] == -2 ? Resolve(sceneId) : arguments[0];
        var args = arguments.ToArray();
        // -1 clears the invoking scene's trigger cell even when changing a remote event.
        // Relocated event cells belong to their target scene (not a remote numeric index in the active map).
        if (args[1] == -1 && SceneDefinition.Contains(triggerPosition))
        {
            Scene(sceneId).SetTile(MapLayer.Event, triggerPosition, -1);
            args[1] = eventContext;
        }
        Scene(target).ChangeEvent(target, eventContext, triggerPosition, args);
    }

    public void IncrementEventScripts(int sceneId, int eventContext, int targetEvent, int interaction, int item, int arrival)
    {
        var index = targetEvent == -2 ? eventContext : targetEvent;
        var deltas = new[] { interaction, item, arrival };
        // DOS arithmetic wraps signed 16-bit words; -2 here is a real result, not a keep-value sentinel.
        var values = Enumerable.Range(0, 3).Select(field => unchecked((short)(EventField(sceneId, index, field + 2) + deltas[field]))).ToArray();
        for (var field = 0; field < 3; field++) SetEventField(sceneId, index, field + 2, values[field]);
    }

    public int ReplaceTiles(int sceneId, MapLayer layer, int from, int to)
    {
        var scene = Scene(sceneId);
        scene.ValidateTile(layer, new(0, 0), from);
        scene.ValidateTile(layer, new(0, 0), to);
        var changed = 0;
        for (var y = 0; y < SceneDefinition.Extent; y++)
        for (var x = 0; x < SceneDefinition.Extent; x++)
        {
            var position = new GridPosition(x, y);
            if (scene.At(layer, position) != from) continue;
            scene.SetTile(layer, position, to);
            changed++;
        }
        return changed;
    }

    public void OpenEntrance(int sceneId)
    {
        sceneId = Resolve(sceneId);
        if (_definition?.HasScene(sceneId) == true && !_definition.Scene(sceneId).HasLegacyMetadata) return;
        SetMetadataWord(sceneId, 9, 0);
    }

    public void OpenAllEntrances()
    {
        foreach (var sceneId in SceneIds) OpenEntrance(sceneId);
        foreach (var (sceneId, condition) in new[] { (2, 2), (38, 2), (75, 1), (80, 1) })
            if (HasScene(sceneId)) SetMetadataWord(sceneId, 9, condition);
    }

    public bool HaveFourteenBooks(int sceneId = -2) => Enumerable.Range(11, 14).All(index => EventField(sceneId, index, 7) == 4664);

    /// <summary>Opcode 59's map side effects only; the session separately removes party members.</summary>
    public void HideDepartedCompanions()
    {
        foreach (var (sceneId, eventIndex) in DepartedCompanionEvents)
            if (HasScene(sceneId)) ChangeEvent(CurrentSceneId, eventIndex, new(-1, -1),
                [sceneId, eventIndex, 0, 0, -1, -1, -1, -1, -1, -1, 0, -2, -2]);
    }

    /// <summary>Opcode 65; selectedIndex must come from the session's existing RNG, in range [0,5).</summary>
    public void MoveWeiXiaobaoShop(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= ShopTargets.Length) throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        var target = ShopTargets[selectedIndex];
        // Check before hiding the old shop: partial scene packages must not silently lose the shop.
        _ = Scene(target.Scene);
        var first = Array.FindIndex(ShopTargets, pair => pair.Scene == CurrentSceneId);
        if (first >= 0)
        {
            var old = ShopTargets[first];
            for (var index = old.Event; index < old.Event + (old.Scene == 61 ? 4 : 3); index++)
                ChangeEvent(CurrentSceneId, index, new(-1, -1),
                    [-2, index, 0, 0, -1, -1, -1, -1, -1, -1, -2, -2, -2]);
        }
        ChangeEvent(CurrentSceneId, target.Event, new(-1, -1),
            [target.Scene, target.Event, 1, 1, 938, -1, -1, 8256, 8256, 8256, -2, -2, -2]);
    }
}
