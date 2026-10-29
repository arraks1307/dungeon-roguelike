using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

public enum Tile
{
    Wall,
    Floor,
    Stairs,
}

/// <summary>
/// Логическая карта подземелья: только данные и алгоритмы, никакой графики.
/// Координата клетки (x, y) на карте соответствует точке (x, 0, y) в 3D-мире.
/// </summary>
public class DungeonMap
{
    public static readonly Vector2I[] Directions =
    {
        new(0, -1),
        new(0, 1),
        new(-1, 0),
        new(1, 0),
    };

    public int Width { get; }
    public int Height { get; }
    public Tile[,] Tiles { get; }
    public bool[,] Explored { get; }
    public List<Rect2I> Rooms { get; } = new();
    public Vector2I StartPos { get; set; }
    public Vector2I StairsPos { get; set; }

    public DungeonMap(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new Tile[width, height]; // по умолчанию всё — стены (Tile.Wall = 0)
        Explored = new bool[width, height];
    }

    public bool InBounds(Vector2I p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public Tile Get(Vector2I p) => InBounds(p) ? Tiles[p.X, p.Y] : Tile.Wall;

    public bool IsWalkable(Vector2I p) => Get(p) != Tile.Wall;

    /// <summary>
    /// Поле зрения: все клетки в радиусе, до которых есть прямая видимость.
    /// </summary>
    public HashSet<Vector2I> ComputeFov(Vector2I origin, int radius)
    {
        var visible = new HashSet<Vector2I>();
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                var p = new Vector2I(origin.X + dx, origin.Y + dy);
                if (InBounds(p) && HasLineOfSight(origin, p))
                    visible.Add(p);
            }
        }
        return visible;
    }

    /// <summary>
    /// Алгоритм Брезенхэма: идём по клеткам отрезка и проверяем, что между точками нет стен.
    /// Сама конечная клетка может быть стеной — стену мы видим, но не то, что за ней.
    /// </summary>
    private bool HasLineOfSight(Vector2I from, Vector2I to)
    {
        int x = from.X, y = from.Y;
        int dx = Math.Abs(to.X - x), dy = -Math.Abs(to.Y - y);
        int sx = x < to.X ? 1 : -1, sy = y < to.Y ? 1 : -1;
        int error = dx + dy;

        while (true)
        {
            if (x == to.X && y == to.Y)
                return true;
            if ((x != from.X || y != from.Y) && Tiles[x, y] == Tile.Wall)
                return false;

            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y += sy;
            }
        }
    }

    /// <summary>
    /// Поиск в ширину (BFS) от точки: для каждой достижимой клетки — сколько шагов до неё.
    /// Враги используют это поле, чтобы идти к игроку кратчайшим путём.
    /// </summary>
    public Dictionary<Vector2I, int> DistanceField(Vector2I start, int maxDistance)
    {
        var distances = new Dictionary<Vector2I, int> { [start] = 0 };
        var queue = new Queue<Vector2I>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int distance = distances[current];
            if (distance >= maxDistance)
                continue;

            foreach (var dir in Directions)
            {
                var next = current + dir;
                if (!IsWalkable(next) || distances.ContainsKey(next))
                    continue;
                distances[next] = distance + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }
}
