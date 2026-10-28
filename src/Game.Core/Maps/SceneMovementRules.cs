namespace JinYong.Core.Maps;

public enum MovementBlock { None, Boundary, Building, Height, Event, Terrain, Story }

public static class SceneMovementRules
{
    private static readonly (short Low, short High)[] BlockedTerrain =
    [ (358, 362), (374, 380), (458, 464), (506, 610), (818, 824),
      (838, 838), (934, 936), (1016, 1022), (1324, 1348) ];

    // OpenLegend src/scene/scene.cpp: target_is_walkable / blocked_earth.
    public static MovementBlock Check(ISceneMap scene, GridPosition from, GridPosition target)
    {
        if (!SceneDefinition.Contains(from) || !SceneDefinition.Contains(target)) return MovementBlock.Boundary;
        if (scene.At(MapLayer.Building, target) != 0) return MovementBlock.Building;
        if (scene.At(MapLayer.BuildingHeight, target) - scene.At(MapLayer.BuildingHeight, from) >= 10)
            return MovementBlock.Height;
        if (scene.EventAt(target)?.BlocksMovement == true) return MovementBlock.Event;
        var ground = scene.At(MapLayer.Ground, target);
        return BlockedTerrain.Any(range => ground >= range.Low && ground <= range.High)
            ? MovementBlock.Terrain : MovementBlock.None;
    }
}
