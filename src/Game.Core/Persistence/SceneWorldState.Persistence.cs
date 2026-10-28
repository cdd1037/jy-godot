using JinYong.Core.Persistence;

namespace JinYong.Core.Maps;

public sealed partial class SceneRuntimeState
{
    internal SceneStateSnapshot CaptureState(int sceneId, short[] metadata) => new(sceneId,
        Enumerable.Range(0, SceneDefinition.LayerCount * SceneDefinition.TileCount).Select(index =>
            At((MapLayer)(index / SceneDefinition.TileCount), new(index % 64, index / 64 % 64))).ToArray(),
        _events.Select(point => Enumerable.Range(0, 11).Select(word => point[word]).ToArray()).ToArray(), metadata, Revision);

    internal void RestoreState(SceneStateSnapshot snapshot)
    {
        if (snapshot.Layers is null || snapshot.Events is null || snapshot.Events.Any(words => words is null) || snapshot.Revision < 0)
            throw new ArgumentException("Invalid saved scene data.");
        var validated = new SceneDefinition(Id, Name, Spawn, snapshot.Layers,
            snapshot.Events.Select(words => new SceneEventDefinition(words)));
        _tiles.Clear();
        for (var index = 0; index < snapshot.Layers.Length; index++)
        {
            var layer = (MapLayer)(index / SceneDefinition.TileCount); var point = new GridPosition(index % 64, index / 64 % 64);
            if (validated.At(layer, point) != _definition.At(layer, point)) _tiles.Add((layer, point), validated.At(layer, point));
        }
        for (var index = 0; index < _events.Length; index++) _events[index] = validated.Events[index];
        Revision = snapshot.Revision;
    }
}

public sealed partial class SceneWorldState
{
    internal SceneStateSnapshot[] CaptureState() => _scenes.Keys.Concat(_metadata.Keys).Distinct().Order()
        .Select(id => Scene(id).CaptureState(id, Metadata(id).ToArray())).ToArray();

    internal void RestoreState(IReadOnlyList<SceneStateSnapshot> snapshots, int activeSceneId)
    {
        if (snapshots is null || snapshots.Any(state => state is null) ||
            snapshots.Select(state => state.SceneId).Distinct().Count() != snapshots.Count ||
            !snapshots.Any(state => state.SceneId == activeSceneId)) throw new ArgumentException("Missing or duplicate saved scenes.");
        foreach (var snapshot in snapshots)
        {
            if (snapshot.SceneId < 0 || !HasScene(snapshot.SceneId) || snapshot.Metadata is null || snapshot.Metadata.Length != 26)
                throw new ArgumentException("Saved scene is unavailable or its metadata is invalid.");
            Scene(snapshot.SceneId).RestoreState(snapshot);
            _metadata[snapshot.SceneId] = snapshot.Metadata.ToArray();
        }
        ActivateScene(activeSceneId);
    }
}
