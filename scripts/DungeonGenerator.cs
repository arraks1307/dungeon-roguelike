using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Генератор подземелья: случайные прямоугольные комнаты, соединённые Г-образными коридорами.
/// </summary>
public static class DungeonGenerator
{
    public static DungeonMap Generate(int width, int height, Random rng,
        int maxRooms = 14, int minRoomSize = 4, int maxRoomSize = 9)
    {
        var map = new DungeonMap(width, height);

        for (int attempt = 0; attempt < maxRooms * 6 && map.Rooms.Count < maxRooms; attempt++)
        {
            int roomWidth = rng.Next(minRoomSize, maxRoomSize + 1);
            int roomHeight = rng.Next(minRoomSize, maxRoomSize + 1);
            int x = rng.Next(1, width - roomWidth - 1);
            int y = rng.Next(1, height - roomHeight - 1);
            var room = new Rect2I(x, y, roomWidth, roomHeight);

            // Grow(1) оставляет между комнатами хотя бы одну клетку стены
            if (map.Rooms.Exists(other => other.Grow(1).Intersects(room)))
                continue;

            CarveRoom(map, room);
            if (map.Rooms.Count > 0)
                CarveCorridor(map, Center(map.Rooms[^1]), Center(room), rng);
            map.Rooms.Add(room);
        }

        map.StartPos = Center(map.Rooms[0]);
        map.StairsPos = Center(map.Rooms[^1]);
        map.Tiles[map.StairsPos.X, map.StairsPos.Y] = Tile.Stairs;
        return map;
    }

    public static Vector2I Center(Rect2I room) => room.Position + room.Size / 2;

    private static void CarveRoom(DungeonMap map, Rect2I room)
    {
        for (int x = room.Position.X; x < room.End.X; x++)
            for (int y = room.Position.Y; y < room.End.Y; y++)
                map.Tiles[x, y] = Tile.Floor;
    }

    private static void CarveCorridor(DungeonMap map, Vector2I from, Vector2I to, Random rng)
    {
        // Случайно выбираем, идти сначала по горизонтали или по вертикали
        var corner = rng.Next(2) == 0 ? new Vector2I(to.X, from.Y) : new Vector2I(from.X, to.Y);
        CarveLine(map, from, corner);
        CarveLine(map, corner, to);
    }

    private static void CarveLine(DungeonMap map, Vector2I from, Vector2I to)
    {
        var step = new Vector2I(Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));
        var p = from;
        map.Tiles[p.X, p.Y] = Tile.Floor;
        while (p != to)
        {
            p += step;
            map.Tiles[p.X, p.Y] = Tile.Floor;
        }
    }
}
