using System;
using System.Linq;
using Godot;

namespace Roguelike;

public enum ItemKind
{
    Weapon,
    Armor,
    HealthPotion,
    TeleportScroll,
    FireScroll,
}

/// <summary>
/// Описание предмета. Power означает разное для разных видов:
/// бонус к атаке, бонус к защите, сколько лечит, сколько урона наносит.
/// Weight — относительная частота появления (чем больше, тем чаще).
/// </summary>
public record Item(string Name, ItemKind Kind, int Power, Color Color, int MinFloor, int Weight)
{
    public bool IsEquipment => Kind is ItemKind.Weapon or ItemKind.Armor;

    public string Describe() => Kind switch
    {
        ItemKind.Weapon => $"+{Power} к атаке",
        ItemKind.Armor => $"+{Power} к защите",
        ItemKind.HealthPotion => $"+{Power} здоровья",
        ItemKind.TeleportScroll => "переносит в случайное место этажа",
        ItemKind.FireScroll => $"{Power} урона всем видимым врагам",
        _ => "",
    };
}

/// <summary>Все предметы игры в одном месте — легко добавлять новые и менять баланс.</summary>
public static class ItemCatalog
{
    private static readonly Color Steel = new(0.8f, 0.82f, 0.9f);

    public static readonly Item Dagger = new("Кинжал", ItemKind.Weapon, 2, Steel, 1, 2);
    public static readonly Item Sword = new("Меч", ItemKind.Weapon, 4, Steel, 2, 6);
    public static readonly Item Axe = new("Топор", ItemKind.Weapon, 6, Steel, 4, 5);
    public static readonly Item LeatherArmor = new("Кожаная броня", ItemKind.Armor, 1, new Color(0.6f, 0.4f, 0.2f), 1, 7);
    public static readonly Item ChainMail = new("Кольчуга", ItemKind.Armor, 2, new Color(0.6f, 0.62f, 0.65f), 2, 5);
    public static readonly Item PlateArmor = new("Латы", ItemKind.Armor, 4, new Color(0.85f, 0.85f, 0.9f), 4, 3);
    public static readonly Item HealthPotion = new("Зелье лечения", ItemKind.HealthPotion, 10, new Color(1f, 0.3f, 0.5f), 1, 30);
    public static readonly Item GreatHealthPotion = new("Большое зелье", ItemKind.HealthPotion, 20, new Color(1f, 0.15f, 0.3f), 3, 12);
    public static readonly Item TeleportScroll = new("Свиток телепорта", ItemKind.TeleportScroll, 0, new Color(0.4f, 0.6f, 1f), 1, 10);
    public static readonly Item FireScroll = new("Свиток огня", ItemKind.FireScroll, 8, new Color(1f, 0.5f, 0.1f), 2, 10);

    public static readonly Item[] All =
    {
        Dagger, Sword, Axe, LeatherArmor, ChainMail, PlateArmor,
        HealthPotion, GreatHealthPotion, TeleportScroll, FireScroll,
    };

    /// <summary>
    /// Взвешенный случайный выбор: представь, что предметы стоят на отрезке,
    /// и каждый занимает длину, равную своему Weight. Бросаем точку на отрезок.
    /// </summary>
    public static Item Roll(Random rng, int floor)
    {
        var pool = All.Where(item => item.MinFloor <= floor).ToArray();
        int roll = rng.Next(pool.Sum(item => item.Weight));
        foreach (var item in pool)
        {
            if (roll < item.Weight)
                return item;
            roll -= item.Weight;
        }
        return pool[^1];
    }
}
