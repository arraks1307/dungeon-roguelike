using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

public enum PlayerClass
{
    Warrior,
    Ranger,
    Mage,
}

public enum SkillId
{
    Whirlwind,
    SecondWind,
    AimedShot,
    ArrowRain,
    Fireball,
    FrostNova,
}

/// <summary>Активная способность: срабатывает по клавише F или G и потом перезаряжается за Cooldown атак героя.</summary>
public record SkillDef(SkillId Id, string Name, string Description, int Cooldown);

/// <summary>Класс героя: стартовые характеристики, снаряжение и две способности.</summary>
public record ClassDef(
    PlayerClass Class, string Name, string Description, int MaxHp, int Attack, Color Color,
    ItemTemplate[] StartingItems, SkillDef[] Skills);

/// <summary>Улучшение, которое выбирают при повышении уровня.</summary>
public record Perk(string Name, string Description, Stats Bonus, int CooldownReduction = 0);

public static class ClassCatalog
{
    public static readonly ClassDef Warrior = new(
        PlayerClass.Warrior, "Воин",
        "Крепкий боец ближнего боя. Начинает в кожаной броне.",
        MaxHp: 38, Attack: 3, new Color(0.85f, 0.35f, 0.3f),
        new[] { ItemCatalog.Dagger, ItemCatalog.LeatherArmor, ItemCatalog.HealthPotion },
        new[]
        {
            new SkillDef(SkillId.Whirlwind, "Вихрь", "удар по всем врагам вокруг", 3),
            new SkillDef(SkillId.SecondWind, "Второе дыхание", "восстанавливает 40% здоровья", 5),
        });

    public static readonly ClassDef Ranger = new(
        PlayerClass.Ranger, "Следопыт",
        "Бьёт издалека и видит дальше других.",
        MaxHp: 34, Attack: 3, new Color(0.35f, 0.8f, 0.4f),
        new[] { ItemCatalog.Dagger, ItemCatalog.SightAmulet, ItemCatalog.HealthPotion },
        new[]
        {
            new SkillDef(SkillId.AimedShot, "Меткий выстрел", "стрела в ближайшего врага (атака +3)", 0),
            new SkillDef(SkillId.ArrowRain, "Град стрел", "стрелы во всех видимых врагов", 3),
        });

    public static readonly ClassDef Mage = new(
        PlayerClass.Mage, "Маг",
        "Хрупкий, но сильные заклинания растут с уровнем.",
        MaxHp: 24, Attack: 2, new Color(0.5f, 0.5f, 1f),
        new[] { ItemCatalog.Dagger, ItemCatalog.HealthPotion, ItemCatalog.HealthPotion, ItemCatalog.MapScroll },
        new[]
        {
            new SkillDef(SkillId.Fireball, "Огненный шар", "взрыв по цели и соседям (6 + 2×уровень)", 2),
            new SkillDef(SkillId.FrostNova, "Ледяная нова", "урон и заморозка всех в радиусе 2", 4),
        });

    public static readonly ClassDef[] All = { Warrior, Ranger, Mage };

    public static ClassDef Get(PlayerClass playerClass) => All[(int)playerClass];

    public static readonly Perk[] Perks =
    {
        new("Крепкое здоровье", "+8 к максимальному здоровью", new Stats(MaxHp: 8)),
        new("Сила", "+2 к атаке", new Stats(Attack: 2)),
        new("Стойкость", "+1 к защите", new Stats(Defense: 1)),
        new("Меткость", "+10% шанс критического удара", new Stats(Crit: 10)),
        new("Жажда крови", "+1 лечения за каждый удар", new Stats(Lifesteal: 1)),
        new("Регенерация", "+1 к регенерации", new Stats(Regen: 1)),
        new("Зоркость", "+2 к радиусу обзора", new Stats(Vision: 2)),
        new("Шипастая кожа", "+2 урона шипами атакующим", new Stats(Thorns: 2)),
        new("Огненная кровь", "+2 урона огнём при каждом ударе", new Stats(FireDamage: 2)),
        new("Сосредоточенность", "способностям нужно на 1 атаку меньше", default, CooldownReduction: 1),
    };

    /// <summary>«1 атаку», «3 атаки», «5 атак» — русское окончание зависит от числа.</summary>
    public static string Attacks(int count) => count switch
    {
        1 => "1 атаку",
        >= 2 and <= 4 => $"{count} атаки",
        _ => $"{count} атак",
    };

    public static Perk FindPerk(string name) => Array.Find(Perks, perk => perk.Name == name);

    /// <summary>Три разных случайных улучшения на выбор.</summary>
    public static List<Perk> RollPerks(Random rng) => Perks.OrderBy(_ => rng.Next()).Take(3).ToList();
}
