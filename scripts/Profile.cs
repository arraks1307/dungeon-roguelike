using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Roguelike;

/// <summary>Постоянное улучшение из меню «Наследие».</summary>
public record LegacyUpgrade(string Id, string Name, string Description, int BasePrice, int MaxLevel);

/// <summary>Достижение: разовая награда осколками.</summary>
public record Achievement(string Id, string Name, string Description, int Reward);

/// <summary>Всё, что остаётся после смерти: осколки, улучшения, достижения, увиденные враги.</summary>
public class ProfileData
{
    public int Shards { get; set; }
    public Dictionary<string, int> Levels { get; set; } = new();
    public int TotalKills { get; set; }
    public int BossesKilled { get; set; }
    public int Wins { get; set; }
    public int BestFloor { get; set; }
    public int Revives { get; set; }
    public HashSet<string> Achievements { get; set; } = new();
    public HashSet<string> Seen { get; set; } = new();
}

/// <summary>
/// Прогресс между забегами. Хранится отдельно от сохранения: смерть его не стирает.
/// Бонусы действуют только в одиночной игре: в отряде они дали бы игрокам неравный старт,
/// а осколки за сетевой забег начисляются как обычно.
/// </summary>
public static class Profile
{
    public const string Vitality = "vitality";
    public const string Power = "power";
    public const string Wealth = "wealth";
    public const string Supplies = "supplies";
    public const string Wisdom = "wisdom";

    public static readonly LegacyUpgrade[] Upgrades =
    {
        new(Vitality, "Живучесть", "+4 к здоровью на старте", 15, 5),
        new(Power, "Закалка", "+1 к атаке на старте", 40, 3),
        new(Wealth, "Наследство", "+30 золота на старте", 10, 5),
        new(Supplies, "Запасы", "+1 зелье лечения на старте", 25, 2),
        new(Wisdom, "Мудрость", "выбрать одно улучшение на старте", 60, 2),
    };

    public static readonly Achievement[] Achievements =
    {
        new("floor5", "Первые шаги", "дойти до 5-го этажа", 10),
        new("floor10", "Глубина", "дойти до 10-го этажа", 25),
        new("win", "Победитель", "пройти подземелье", 60),
        new("nightmare", "Кошмар наяву", "пройти подземелье на Кошмаре", 200),
        new("kills", "Гроза подземелья", "победить 500 врагов за всё время", 40),
        new("bosses", "Охотник на боссов", "победить 10 боссов за всё время", 40),
        new("rescue", "Не оставь своих", "поднять союзника 10 раз за всё время", 30),
        new("rich", "Богач", "накопить 1000 золота за забег", 20),
    };

    /// <summary>Самопроверка подменяет путь и не трогает настоящий прогресс игрока.</summary>
    public static string FilePath { get; set; } = "user://profile.json";

    public static ProfileData Current { get; private set; } = new();

    public static void Load()
    {
        if (!FileAccess.FileExists(FilePath))
        {
            Current = new ProfileData();
            return;
        }
        try
        {
            Current = JsonSerializer.Deserialize<ProfileData>(FileAccess.GetFileAsString(FilePath)) ?? new ProfileData();
        }
        catch (JsonException)
        {
            Current = new ProfileData();
        }
    }

    public static void Save()
    {
        using var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(Current));
    }

    public static void Delete()
    {
        if (FileAccess.FileExists(FilePath))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FilePath));
        Current = new ProfileData();
    }

    public static int Level(string id) => Current.Levels.GetValueOrDefault(id);

    /// <summary>Каждый следующий уровень дороже: базовая цена умножается на номер уровня.</summary>
    public static int Price(LegacyUpgrade upgrade) => upgrade.BasePrice * (Level(upgrade.Id) + 1);

    public static bool TryBuy(LegacyUpgrade upgrade)
    {
        int price = Price(upgrade);
        if (Level(upgrade.Id) >= upgrade.MaxLevel || Current.Shards < price)
            return false;
        Current.Shards -= price;
        Current.Levels[upgrade.Id] = Level(upgrade.Id) + 1;
        Save();
        return true;
    }

    /// <summary>Отметить достижение. Возвращает его, если получено впервые, и начисляет награду.</summary>
    public static Achievement Unlock(string id)
    {
        if (!Current.Achievements.Add(id))
            return null;
        var achievement = Achievements.First(item => item.Id == id);
        Current.Shards += achievement.Reward;
        return achievement;
    }
}
