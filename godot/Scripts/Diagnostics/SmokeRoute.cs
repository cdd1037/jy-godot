using JinYong.Core.Maps;

namespace JinYong.Host;

internal static class SmokeRoute
{
    public static List<Direction> Create(ISceneMap scene)
    {
        var queue = new Queue<GridPosition>();
        var paths = new Dictionary<GridPosition, List<Direction>> { [scene.Spawn] = [] };
        queue.Enqueue(scene.Spawn);
        List<Direction>? fallback = null;
        while (queue.TryDequeue(out var point))
        {
            var path = paths[point];
            foreach (var direction in Enum.GetValues<Direction>())
            {
                var next = point.Step(direction);
                if (SceneMovementRules.Check(scene, point, next) != MovementBlock.None)
                {
                    if (path.Count >= 6) return [.. path, direction];
                    if (path.Count > 0) fallback = [.. path, direction];
                    continue;
                }
                if (paths.ContainsKey(next)) continue;
                paths.Add(next, [.. path, direction]);
                queue.Enqueue(next);
            }
        }
        return fallback ?? [];
    }
}
