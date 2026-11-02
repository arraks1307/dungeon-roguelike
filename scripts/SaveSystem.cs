using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace Roguelike;

// ---------- Снимки (DTO) для сохранения ----------
// Узлы Godot нельзя напрямую записать в JSON, поэтому для каждого игрового объекта
// есть простой класс только с числами и строками. Публичные поля вместо свойств —
// для краткости (сериализатору это разрешено опцией IncludeFields).

public class SaveData
{
    public int Version = SaveSystem.CurrentVersion;
    public int Floor;
    public int Kills;
    public PlayerSave Player;
    public MapSave Map;
    public List<EnemySave> Enemies = new();
    public List<PickupSave> Pickups = new();
    public List<ChestSave> Chests = new();
    public MerchantSave Merchant;
}

public class ChestSave
{
    public int X, Y;
    public bool Mimic, Secret;
}

public class TrapSave
{
    public int X, Y;
    public TrapKind Kind;
    public bool Revealed;
}

public class ItemSave
{
    public string Template;
    public Rarity Rarity;
    public string[] Affixes = Array.Empty<string>();
    public int Count = 1;

    public static ItemSave From(Item item) => new()
    {
        Template = item.Template.Name,
        Rarity = item.Rarity,
        Affixes = item.Affixes.Select(affix => affix.Name).ToArray(),
        Count = item.Count,
    };

    /// <summary>Восстановить предмет по названиям. null — если такого шаблона больше нет в игре.</summary>
    public Item ToItem()
    {
        var template = ItemCatalog.All.FirstOrDefault(t => t.Name == Template);
        if (template == null)
            return null;
        var affixes = Affixes
            .Select(name => ItemCatalog.Affixes.FirstOrDefault(affix => affix.Name == name))
            .Where(affix => affix != null)
            .ToArray();
        return new Item(template, Rarity, affixes) { Count = Count };
    }
}

public class PlayerSave
{
    public int X, Y;
    public int Level, Hp, Gold;
    public int BaseAttack, BaseMaxHp;
    public int StrengthTurns, StrengthBonus, InvisibleTurns, TurnCounter;
    public List<ItemSave> Items = new();
    public List<ItemSave> Equipped = new();
}

public class MapSave
{
    public int Width, Height;
    public string Tiles;    // по символу-цифре на клетку: номер значения enum Tile
    public string Explored; // '1' — исследована, '0' — нет
    public List<int[]> Rooms = new(); // [x, y, ширина, высота]
    public int StartX, StartY, StairsX, StairsY;
    public int Theme;
    public List<TrapSave> Traps = new();

    public static MapSave From(DungeonMap map)
    {
        var tiles = new StringBuilder(map.Width * map.Height);
        var explored = new StringBuilder(map.Width * map.Height);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                tiles.Append((char)('0' + (int)map.Tiles[x, y]));
                explored.Append(map.Explored[x, y] ? '1' : '0');
            }
        }
        return new MapSave
        {
            Width = map.Width,
            Height = map.Height,
            Tiles = tiles.ToString(),
            Explored = explored.ToString(),
            Rooms = map.Rooms.Select(r => new[] { r.Position.X, r.Position.Y, r.Size.X, r.Size.Y }).ToList(),
            StartX = map.StartPos.X, StartY = map.StartPos.Y,
            StairsX = map.StairsPos.X, StairsY = map.StairsPos.Y,
            Theme = map.ThemeIndex,
            Traps = map.Traps.Select(pair => new TrapSave
            {
                X = pair.Key.X, Y = pair.Key.Y, Kind = pair.Value.Kind, Revealed = pair.Value.Revealed,
            }).ToList(),
        };
    }

    public DungeonMap ToMap()
    {
        var map = new DungeonMap(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                map.Tiles[x, y] = (Tile)(Tiles[i] - '0');
                map.Explored[x, y] = Explored[i] == '1';
            }
        }
        foreach (var r in Rooms)
            map.Rooms.Add(new Rect2I(r[0], r[1], r[2], r[3]));
        map.StartPos = new Vector2I(StartX, StartY);
        map.StairsPos = new Vector2I(StairsX, StairsY);
        map.ThemeIndex = Math.Clamp(Theme, 0, Themes.All.Length - 1);
        foreach (var trap in Traps)
            map.Traps[new Vector2I(trap.X, trap.Y)] = new Trap { Kind = trap.Kind, Revealed = trap.Revealed };
        return map;
    }
}

public class EnemySave
{
    public string Type;
    public int X, Y, Hp, Frozen;
    public bool Alerted, Boss;
}

public class PickupSave
{
    public int X, Y, Gold;
    public ItemSave Item; // null — значит, это золото
}

public class MerchantSave
{
    public int X, Y;
    public List<ItemSave> Stock = new();
}

/// <summary>
/// Запись и чтение сохранения. Сохранение одно (как в классических рогаликах):
/// оно продолжается с того же места и удаляется после смерти.
/// </summary>
public static class SaveSystem
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    /// <summary>Путь можно подменить — самопроверка пишет в отдельный файл, не трогая настоящий.</summary>
    public static string FilePath { get; set; } = "user://savegame.json";

    public static bool HasSave => FileAccess.FileExists(FilePath);

    public static void Save(SaveData data)
    {
        using var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(data, Options));
    }

    /// <summary>Возвращает null, если сохранения нет, оно повреждено или от несовместимой версии.</summary>
    public static SaveData Load()
    {
        if (!HasSave)
            return null;
        try
        {
            var data = JsonSerializer.Deserialize<SaveData>(FileAccess.GetFileAsString(FilePath), Options);
            return data?.Version == CurrentVersion ? data : null;
        }
        catch (Exception exception) when (exception is JsonException or IndexOutOfRangeException or NullReferenceException)
        {
            GD.PushWarning($"Сохранение повреждено: {exception.Message}");
            return null;
        }
    }

    public static void Delete()
    {
        if (HasSave)
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FilePath));
    }
}
