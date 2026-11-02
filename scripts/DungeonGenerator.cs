using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Генератор этажей двух видов:
/// - классический: случайные прямоугольные комнаты, Г-образные коридоры, двери и секретная комната;
/// - пещеры: клеточный автомат (как «Жизнь» Конвея), который превращает шум в естественные пещеры.
/// </summary>
public static class DungeonGenerator
{
    private const int BossRoomWidth = 12;
    private const int BossRoomHeight = 9;
    private const double DoorChance = 0.5;
    private const double SecretRoomChance = 0.6;

    /// <summary>
    /// withBossRoom — добавить большую комнату для босса (всегда классический этаж).
    /// Лестница всегда в последней комнате.
    /// </summary>
    public static DungeonMap Generate(int width, int height, Random rng, int themeIndex, bool withBossRoom = false)
    {
        var theme = Themes.All[themeIndex];
        var map = theme.Caves && !withBossRoom
            ? GenerateCaves(width, height, rng)
            : GenerateRooms(width, height, rng, withBossRoom);
        map.ThemeIndex = themeIndex;
        return map;
    }

    public static Vector2I Center(Rect2I room) => room.Position + room.Size / 2;

    // ---------- Классический этаж ----------

    private static DungeonMap GenerateRooms(int width, int height, Random rng, bool withBossRoom,
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

        if (withBossRoom)
            TryAddBossRoom(map, rng);

        map.StartPos = Center(map.Rooms[0]);
        map.StairsPos = Center(map.Rooms[^1]);
        map.Tiles[map.StairsPos.X, map.StairsPos.Y] = Tile.Stairs;

        // Двери не ставим в зал босса: он должен быть виден сразу
        int roomsWithDoors = withBossRoom ? map.Rooms.Count - 1 : map.Rooms.Count;
        for (int i = 0; i < roomsWithDoors; i++)
            AddDoors(map, map.Rooms[i], rng);

        if (!withBossRoom && rng.NextDouble() < SecretRoomChance)
            TryAddSecretRoom(map, rng);
        return map;
    }

    /// <summary>
    /// Большой зал ставим туда, где он не пересекается с комнатами. Коридоры его пересекать могут —
    /// это не страшно, они просто станут входами в зал. Если места не нашлось, боссом станет последняя комната.
    /// </summary>
    private static void TryAddBossRoom(DungeonMap map, Random rng)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            int x = rng.Next(1, map.Width - BossRoomWidth - 1);
            int y = rng.Next(1, map.Height - BossRoomHeight - 1);
            var room = new Rect2I(x, y, BossRoomWidth, BossRoomHeight);
            if (map.Rooms.Exists(other => other.Grow(1).Intersects(room)))
                continue;

            CarveRoom(map, room);
            CarveCorridor(map, Center(map.Rooms[^1]), Center(room), rng);
            map.Rooms.Add(room);
            return;
        }
    }

    /// <summary>
    /// Дверь ставится в проём: клетка пола сразу за краем комнаты, по бокам от которой — стены.
    /// Так дверь окажется именно в узком входе, а не посреди широкого прохода.
    /// </summary>
    private static void AddDoors(DungeonMap map, Rect2I room, Random rng)
    {
        var ring = new List<(Vector2I Cell, bool Horizontal)>();
        for (int x = room.Position.X; x < room.End.X; x++)
        {
            ring.Add((new Vector2I(x, room.Position.Y - 1), true));
            ring.Add((new Vector2I(x, room.End.Y), true));
        }
        for (int y = room.Position.Y; y < room.End.Y; y++)
        {
            ring.Add((new Vector2I(room.Position.X - 1, y), false));
            ring.Add((new Vector2I(room.End.X, y), false));
        }

        foreach (var (cell, horizontal) in ring)
        {
            if (map.Get(cell) != Tile.Floor || rng.NextDouble() > DoorChance)
                continue;
            // Для верхнего/нижнего края стены должны быть слева и справа, для боковых — сверху и снизу
            var side = horizontal ? new Vector2I(1, 0) : new Vector2I(0, 1);
            if (map.Get(cell + side) != Tile.Wall || map.Get(cell - side) != Tile.Wall)
                continue;
            if (DungeonMap.Directions.Any(d => map.IsDoor(cell + d)))
                continue; // две двери рядом выглядят странно
            map.Tiles[cell.X, cell.Y] = Tile.Door;
        }
    }

    /// <summary>
    /// Секретная комната 4×4 вплотную к обычной, через одну клетку стены.
    /// Эта клетка становится потайной дверью. Пробуем комнаты и стороны в случайном порядке.
    /// </summary>
    private static void TryAddSecretRoom(DungeonMap map, Random rng)
    {
        const int size = 4;
        foreach (var room in map.Rooms.Skip(1).OrderBy(_ => rng.Next()))
        {
            foreach (int side in Enumerable.Range(0, 4).OrderBy(_ => rng.Next()))
            {
                Rect2I secret;
                Vector2I door;
                if (side < 2) // справа или слева
                {
                    int y = room.Position.Y + rng.Next(Math.Max(1, room.Size.Y - size + 1));
                    int x = side == 0 ? room.End.X + 1 : room.Position.X - 1 - size;
                    secret = new Rect2I(x, y, size, size);
                    int doorY = Math.Max(room.Position.Y, y) + 1;
                    if (doorY >= Math.Min(room.End.Y, y + size))
                        doorY = Math.Max(room.Position.Y, y);
                    door = new Vector2I(side == 0 ? room.End.X : room.Position.X - 1, doorY);
                }
                else // снизу или сверху
                {
                    int x = room.Position.X + rng.Next(Math.Max(1, room.Size.X - size + 1));
                    int y = side == 2 ? room.End.Y + 1 : room.Position.Y - 1 - size;
                    secret = new Rect2I(x, y, size, size);
                    int doorX = Math.Max(room.Position.X, x) + 1;
                    if (doorX >= Math.Min(room.End.X, x + size))
                        doorX = Math.Max(room.Position.X, x);
                    door = new Vector2I(doorX, side == 2 ? room.End.Y : room.Position.Y - 1);
                }

                if (!IsSolidRock(map, secret.Grow(1)) || map.Get(door) != Tile.Wall)
                    continue;

                CarveRoom(map, secret);
                map.Tiles[door.X, door.Y] = Tile.SecretDoor;
                map.SecretRooms.Add(secret);
                return;
            }
        }
    }

    /// <summary>Весь прямоугольник — сплошной камень внутри карты (не задевает комнаты и коридоры).</summary>
    private static bool IsSolidRock(DungeonMap map, Rect2I area)
    {
        if (area.Position.X < 0 || area.Position.Y < 0 || area.End.X > map.Width || area.End.Y > map.Height)
            return false;
        for (int x = area.Position.X; x < area.End.X; x++)
            for (int y = area.Position.Y; y < area.End.Y; y++)
                if (map.Tiles[x, y] != Tile.Wall)
                    return false;
        return true;
    }

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

    // ---------- Пещеры ----------

    /// <summary>
    /// 1. Заполняем карту случайным шумом: 45% стен.
    /// 2. Несколько раз «сглаживаем»: клетка становится стеной, если вокруг неё 5+ стен из 8.
    ///    Одиночные стены исчезают, скопления растут — получаются плавные пещеры.
    /// 3. Оставляем только самую большую связную область, остальные заделываем.
    /// </summary>
    private static DungeonMap GenerateCaves(int width, int height, Random rng)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var walls = new bool[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    walls[x, y] = x == 0 || y == 0 || x == width - 1 || y == height - 1 || rng.NextDouble() < 0.45;

            for (int step = 0; step < 5; step++)
                walls = Smooth(walls, width, height);

            var map = new DungeonMap(width, height);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    map.Tiles[x, y] = walls[x, y] ? Tile.Wall : Tile.Floor;

            var region = LargestRegion(map);
            if (region.Count < width * height * 0.3)
                continue; // пещера получилась слишком маленькой — пробуем ещё раз

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (!region.Contains(new Vector2I(x, y)))
                        map.Tiles[x, y] = Tile.Wall;

            PlaceCaveStartAndStairs(map, region, rng);
            return map;
        }
        return GenerateRooms(width, height, rng, withBossRoom: false); // на всякий случай
    }

    private static bool[,] Smooth(bool[,] walls, int width, int height)
    {
        var result = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    result[x, y] = true;
                    continue;
                }
                int count = 0;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if ((dx != 0 || dy != 0) && walls[x + dx, y + dy])
                            count++;
                result[x, y] = count >= 5 || (count == 4 && walls[x, y]);
            }
        }
        return result;
    }

    /// <summary>Заливка (flood fill): находим все связные области пола и возвращаем самую большую.</summary>
    private static HashSet<Vector2I> LargestRegion(DungeonMap map)
    {
        var seen = new HashSet<Vector2I>();
        var best = new HashSet<Vector2I>();
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var start = new Vector2I(x, y);
                if (map.Tiles[x, y] != Tile.Floor || seen.Contains(start))
                    continue;

                var region = new HashSet<Vector2I> { start };
                var queue = new Queue<Vector2I>();
                queue.Enqueue(start);
                seen.Add(start);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var dir in DungeonMap.Directions.Take(4))
                    {
                        var next = current + dir;
                        if (map.Get(next) == Tile.Floor && seen.Add(next))
                        {
                            region.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
                if (region.Count > best.Count)
                    best = region;
            }
        }
        return best;
    }

    /// <summary>
    /// В пещере нет настоящих комнат, поэтому делаем «псевдокомнаты» 5×5 вокруг случайных клеток пола —
    /// по ним остальной код расставляет врагов и предметы. Лестница — в самой дальней от старта точке.
    /// </summary>
    private static void PlaceCaveStartAndStairs(DungeonMap map, HashSet<Vector2I> region, Random rng)
    {
        var cells = region.ToList();
        var start = cells[rng.Next(cells.Count)];
        var distances = map.DistanceField(start, map.Width * map.Height);
        var stairs = distances.OrderByDescending(pair => pair.Value).First().Key;

        map.Rooms.Add(RoomAround(map, start));
        foreach (var cell in cells.OrderBy(_ => rng.Next()).Take(12))
            map.Rooms.Add(RoomAround(map, cell));
        map.Rooms.Add(RoomAround(map, stairs));

        map.StartPos = start;
        map.StairsPos = stairs;
        map.Tiles[stairs.X, stairs.Y] = Tile.Stairs;
    }

    private static Rect2I RoomAround(DungeonMap map, Vector2I cell)
    {
        int x = Math.Clamp(cell.X - 2, 1, map.Width - 6);
        int y = Math.Clamp(cell.Y - 2, 1, map.Height - 6);
        return new Rect2I(x, y, 5, 5);
    }
}
