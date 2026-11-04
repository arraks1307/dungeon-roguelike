using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Roguelike;

/// <summary>Постоянное улучшение из меню «Наследие».</summary>
public record LegacyUpgrade(string Id, string Name, string Description, int BasePrice, int MaxLevel, string Requires = null);

/// <summary>Достижение: разовая награда осколками.</summary>
public record Achievement(string Id, string Name, string Description, int Reward, string Prize = null);

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
    public HashSet<string> Rescued { get; set; } = new();
    public HashSet<int> Notes { get; set; } = new();
    public HashSet<string> ClassWins { get; set; } = new();
    public bool IntroSeen { get; set; }
    public int LastClass { get; set; }
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
    public const string Forge = "forge";
    public const string Garden = "garden";
    public const string MapTable = "maptable";
    public const string Stall = "stall";
    public const string Archive = "archive";

    public static readonly LegacyUpgrade[] Upgrades =
    {
        new(Vitality, "Живучесть", "+4 к здоровью на старте", 15, 5),
        new(Power, "Закалка", "+1 к атаке на старте", 40, 3),
        new(Wealth, "Наследство", "+30 золота на старте", 10, 5),
        new(Supplies, "Запасы", "+1 зелье лечения на старте", 25, 2),
        new(Wisdom, "Мудрость", "выбрать одно улучшение на старте", 60, 2),
        new(Forge, "Кузница", "стартовое оружие заточено на +1", 35, 3, "smith"),
        new(Garden, "Сад трав", "+1 большое зелье на старте", 30, 2, "herbalist"),
        new(MapTable, "Стол картографа", "+1 свиток карты на старте", 20, 2, "cartographer"),
        new(Stall, "Лавка Саны", "+60 золота и бомба на старте", 30, 2, "trader"),
        new(Archive, "Архив", "+10% осколков за забег", 50, 3, "chronicler"),
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
        new("pact5", "Продажная душа", "заключить 5 сделок за один забег", 40, "класс Берсерк и Коса жнеца"),
        new("collector", "Полный комплект", "надеть набор целиком", 40, "Кольцо эха"),
        new("noarmor", "Голый король", "победить босса без брони и шлема", 50, "Сердце голема"),
        new("bareboss", "Голыми руками", "победить босса без оружия", 60, "Копьё пронзания"),
        new("lavawalker", "Горячие ступни", "наступить на лаву 10 раз за забег", 25, "Чешуя саламандры"),
        new("beggar", "Скряга", "дойти до 6-го этажа, не потратив ни монеты", 30),
        new("alchemist", "Алхимик", "выпить 15 зелий за забег", 25),
        new("overkill", "Перебор", "нанести 100 урона одним ударом", 30),
        new("firstfloor", "Неудачное начало", "погибнуть на первом этаже", 5),
        new("richcorpse", "С собой не унесёшь", "погибнуть с 500 золота в кармане", 15),
        new("allresidents", "Дом полон", "спасти всех жителей лагеря", 50),
        new("allnotes", "Летописец", "собрать все записи хрониста", 50),
        new("cleanhands", "Чистые руки", "победить, не заключив ни одной сделки", 80),
        new("soulless", "Без остатка", "победить с 8 сделками за забег", 100),
        new("master", "Мастер всех школ", "победить каждым классом", 150),
        new("trueending", "Истинный финал", "закрыть Печать навсегда", 200),
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

    public static bool Has(string achievement) => Current.Achievements.Contains(achievement);

    public static string AchievementDescription(string id) => Achievements.First(item => item.Id == id).Description;

    /// <summary>Постройка доступна, когда спасён житель, который её ведёт.</summary>
    public static bool IsOpen(LegacyUpgrade upgrade) => upgrade.Requires == null || Current.Rescued.Contains(upgrade.Requires);

    /// <summary>Каждый следующий уровень дороже: базовая цена умножается на номер уровня.</summary>
    public static int Price(LegacyUpgrade upgrade) => upgrade.BasePrice * (Level(upgrade.Id) + 1);

    public static bool TryBuy(LegacyUpgrade upgrade)
    {
        int price = Price(upgrade);
        if (!IsOpen(upgrade) || Level(upgrade.Id) >= upgrade.MaxLevel || Current.Shards < price)
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
        Worlds.AddUnlock(id); // классы выбираются до лагеря, поэтому их открытия общие для всех миров
        Current.Shards += achievement.Reward;
        return achievement;
    }
}
