namespace JinYong.Core.Maps;

public readonly record struct GridPosition(int X, int Y)
{
    public GridPosition Step(Direction direction) => direction switch
    {
        Direction.Up => this with { Y = Y - 1 },
        Direction.Right => this with { X = X + 1 },
        Direction.Left => this with { X = X - 1 },
        Direction.Down => this with { Y = Y + 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
}

// Numeric values preserve OpenLegend's scene direction and player frame mapping.
public enum Direction { Up, Right, Left, Down }
public enum MapLayer { Ground, Building, Decoration, Event, BuildingHeight, DecorationHeight }

public static class IsometricProjection
{
    public static (int X, int Y) Project(GridPosition position, int height = 0) =>
        ((position.X - position.Y) * 18, (position.X + position.Y) * 9 - height);
}
