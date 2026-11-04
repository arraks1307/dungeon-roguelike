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
    Priest,
    Rogue,
}

public enum SkillId
{
    Whirlwind,
    SecondWind,
    AimedShot,
    ArrowRain,
    Fireball,
    FrostNova,
    Smite,
    Mend,
    Backstab,
    Smoke,
}

/// <summary>Активная способность: срабатывает по клавише F или G и потом перезаряжается за Cooldown атак героя.</summary>
public record SkillDef(SkillId Id, string Name, string Description, int Cooldown);

/// <summary>Класс героя: стартовые характеристики, снаряжение и две способности.</summary>
public record ClassDef(
    PlayerClass Class, string Name, string Description, int MaxHp, int Attack, Color Color,
    ItemTemplate[] StartingItems, SkillDef[] Skills);

/// <summary>Улучшение, которое выбирают при повышении уровня.</summary>
public record Perk(string Name, string Description, Stats Bonus, int CooldownReduction = 0,
    PlayerClass? Only = null, PerkEffect Effect = PerkEffect.None);

/// <summary>Улучшения, которые меняют правила, а не просто добавляют числа.</summary>
public enum PerkEffect
{
    None,
    Cleave,    // удар задевает всех врагов рядом
    Rage,      // чем меньше здоровья, тем сильнее удар
    Ricochet,  // меткий выстрел попадает ещё и в соседнюю цель
    Hunter,    // первый удар по невредимому врагу сильнее
    Pyromancer, // огненный шар взрывается шире
    Arcane,    // убийство заряжает способности
    Sentinel,  // стойка держит втрое больше
    Grace,     // исцеление лечит ещё и всех рядом с целью
    Faith,     // кара бьёт сильнее и лечит вдвое больше
    Shatter,   // заморозка мага делает врага хрупким и для него самого
    Assassin,  // убийство заряжает способности сразу
}

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

    public static readonly ClassDef Priest = new(
        PlayerClass.Priest, "Жрец",
        "Лечит союзников и бьёт светом. Сильнее всего в отряде.",
        MaxHp: 30, Attack: 2, new Color(0.95f, 0.8f, 0.4f),
        new[] { ItemCatalog.Dagger, ItemCatalog.HealthPotion, ItemCatalog.HealthPotion },
        new[]
        {
            new SkillDef(SkillId.Smite, "Кара", "свет бьёт ближайшего врага и лечит всех рядом с тобой", 1),
            new SkillDef(SkillId.Mend, "Исцеление", "лечит самого раненого героя отряда на 35%", 3),
        });

    public static readonly ClassDef Rogue = new(
        PlayerClass.Rogue, "Разбойник",
        "Бьёт из тени: чем незаметнее, тем страшнее удар.",
        MaxHp: 28, Attack: 4, new Color(0.65f, 0.5f, 0.85f),
        new[] { ItemCatalog.Dagger, ItemCatalog.HealthPotion, ItemCatalog.InvisibilityPotion },
        new[]
        {
            new SkillDef(SkillId.Backstab, "Удар в спину", "вдвое сильнее, из невидимости втрое", 2),
            new SkillDef(SkillId.Smoke, "Дымовая завеса", "невидимость на 3 хода", 4),
        });

    public static readonly ClassDef[] All = { Warrior, Ranger, Mage, Priest, Rogue };

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
        new("Страж", "стойка пропускает только четверть урона", default, Effect: PerkEffect.Sentinel),
        new("Поток силы", "каждое убийство заряжает способности", default, Effect: PerkEffect.Arcane),
        new("Рассекающий удар", "удар задевает всех врагов рядом с тобой", default, Only: PlayerClass.Warrior, Effect: PerkEffect.Cleave),
        new("Ярость", "+1 к удару за каждую потерянную четверть здоровья", default, Only: PlayerClass.Warrior, Effect: PerkEffect.Rage),
        new("Рикошет", "меткий выстрел попадает и в ближайшую к цели", default, Only: PlayerClass.Ranger, Effect: PerkEffect.Ricochet),
        new("Охотник", "+4 к удару по невредимому врагу", default, Only: PlayerClass.Ranger, Effect: PerkEffect.Hunter),
        new("Пиромант", "огненный шар взрывается шире", default, Only: PlayerClass.Mage, Effect: PerkEffect.Pyromancer),
        new("Ледяной осколок", "замороженные враги получают от тебя двойной урон", default, Only: PlayerClass.Mage, Effect: PerkEffect.Shatter),
        new("Убийца", "убийство заряжает все способности полностью", default, Only: PlayerClass.Rogue, Effect: PerkEffect.Assassin),
        new("Благодать", "исцеление лечит и всех рядом с целью", default, Only: PlayerClass.Priest, Effect: PerkEffect.Grace),
        new("Свет веры", "+3 к урону кары, и лечит она вдвое сильнее", default, Only: PlayerClass.Priest, Effect: PerkEffect.Faith),
    };

    /// <summary>«1 атаку», «3 атаки», «5 атак» — русское окончание зависит от числа.</summary>
    public static string Attacks(int count) => count switch
    {
        1 => "1 атаку",
        >= 2 and <= 4 => $"{count} атаки",
        _ => $"{count} атак",
    };

    public static Perk FindPerk(string name) => Array.Find(Perks, perk => perk.Name == name);

    /// <summary>
    /// Три разных улучшения на выбор: подходят классу героя, правила меняются только один раз,
    /// и если доступно особое улучшение, одно такое всегда попадает в тройку.
    /// </summary>
    public static List<Perk> RollPerks(Random rng, Player hero)
    {
        var pool = Perks
            .Where(perk => perk.Only == null || perk.Only == hero.Class.Class)
            .Where(perk => perk.Effect == PerkEffect.None || !hero.Has(perk.Effect))
            .OrderBy(_ => rng.Next())
            .ToList();
        var special = pool.FirstOrDefault(perk => perk.Effect != PerkEffect.None);
        var result = pool.Where(perk => perk != special).Take(special == null ? 3 : 2).ToList();
        if (special != null)
            result.Insert(0, special);
        return result;
    }
}
