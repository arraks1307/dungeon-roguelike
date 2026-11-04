using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

public enum ItemKind
{
    Weapon,
    Armor,
    Helmet,
    Ring,
    Amulet,
    HealthPotion,
    StrengthPotion,
    InvisibilityPotion,
    TeleportScroll,
    FireScroll,
    FreezeScroll,
    MapScroll,
    Bomb,
}

public enum EquipSlot
{
    None,
    Weapon,
    Armor,
    Helmet,
    Ring,
    Amulet,
}

public enum Rarity
{
    Common,
    Rare,
    Epic,
    Legendary,
}

/// <summary>Особое правило легендарной вещи.</summary>
public enum LegendaryEffect
{
    None,
    Storm,   // каждая третья атака бьёт всех вокруг
    Greed,   // золото превращается в урон
    Phoenix, // раз за этаж спасает от смерти
    Shadow,  // после победы герой исчезает на два хода
}

/// <summary>
/// Набор бонусов к характеристикам. record struct — лёгкая структура с готовым сравнением,
/// а перегруженные операторы + и − позволяют складывать бонусы всей экипировки и сравнивать вещи.
/// </summary>
public readonly record struct Stats(
    int Attack = 0, int Defense = 0, int MaxHp = 0, int Crit = 0, int Lifesteal = 0,
    int Thorns = 0, int Regen = 0, int Vision = 0, int FireDamage = 0)
{
    public static Stats operator +(Stats a, Stats b) => new(
        a.Attack + b.Attack, a.Defense + b.Defense, a.MaxHp + b.MaxHp, a.Crit + b.Crit, a.Lifesteal + b.Lifesteal,
        a.Thorns + b.Thorns, a.Regen + b.Regen, a.Vision + b.Vision, a.FireDamage + b.FireDamage);

    public static Stats operator -(Stats a, Stats b) => new(
        a.Attack - b.Attack, a.Defense - b.Defense, a.MaxHp - b.MaxHp, a.Crit - b.Crit, a.Lifesteal - b.Lifesteal,
        a.Thorns - b.Thorns, a.Regen - b.Regen, a.Vision - b.Vision, a.FireDamage - b.FireDamage);

    /// <summary>Текстовые строки для ненулевых бонусов, например «+2 к атаке».</summary>
    public List<string> Describe() => DescribeSigned().Select(part => part.Text).ToList();

    /// <summary>То же, но вместе со значением — по знаку окна красят строку в зелёный или красный.</summary>
    public List<(int Value, string Text)> DescribeSigned()
    {
        var parts = new List<(int, string)>();
        void Add(int value, string text)
        {
            if (value != 0)
                parts.Add((value, $"{(value > 0 ? "+" : "−")}{Math.Abs(value)}{text}"));
        }
        Add(Attack, " к атаке");
        Add(Defense, " к защите");
        Add(MaxHp, " к здоровью");
        Add(Crit, "% шанс крита");
        Add(Lifesteal, " лечения за удар");
        Add(Thorns, " урона шипами");
        Add(Regen, " регенерации");
        Add(Vision, " к обзору");
        Add(FireDamage, " урона огнём");
        return parts;
    }
}

/// <summary>
/// Шаблон предмета: «что это за вещь». Stats — бонусы экипировки,
/// Power — сила расходника (сколько лечит, сколько ходов действует и т. п.).
/// Weight — относительная частота появления, Price — базовая цена у торговца.
/// </summary>
public record ItemTemplate(
    string Name, ItemKind Kind, Stats Stats, int Power, Color Color, int MinFloor, int Weight, int Price,
    LegendaryEffect Effect = LegendaryEffect.None, string Special = null)
{
    public EquipSlot Slot => Kind switch
    {
        ItemKind.Weapon => EquipSlot.Weapon,
        ItemKind.Armor => EquipSlot.Armor,
        ItemKind.Helmet => EquipSlot.Helmet,
        ItemKind.Ring => EquipSlot.Ring,
        ItemKind.Amulet => EquipSlot.Amulet,
        _ => EquipSlot.None,
    };
}

/// <summary>Случайное свойство предмета. Название — существительное в родительном падеже: «Меч вампира».</summary>
public record Affix(string Name, Stats Stats);

/// <summary>
/// Конкретный предмет: шаблон + редкость + случайные свойства + количество в стопке.
/// Два «Меча» могут отличаться, поэтому это отдельный класс, а не просто шаблон.
/// </summary>
public class Item
{
    public const int MaxStack = 9;

    public ItemTemplate Template { get; }
    public Rarity Rarity { get; }
    public IReadOnlyList<Affix> Affixes { get; }
    public int Count { get; set; } = 1;

    /// <summary>Уровень заточки у торговца: ко всем бонусам вещи прибавляется UpgradeStats.</summary>
    public int Upgrade { get; set; }

    public Item(ItemTemplate template, Rarity rarity = Rarity.Common, IReadOnlyList<Affix> affixes = null)
    {
        Template = template;
        Rarity = rarity;
        Affixes = affixes ?? Array.Empty<Affix>();
    }

    public ItemKind Kind => Template.Kind;
    public EquipSlot Slot => Template.Slot;
    public bool IsEquipment => Slot != EquipSlot.None;
    public bool IsStackable => !IsEquipment;
    public Color Color => Template.Color;

    public string Name => (Affixes.Count == 0
        ? Template.Name
        : $"{Template.Name} {string.Join(" и ", Affixes.Select(affix => affix.Name))}")
        + (Upgrade > 0 ? $" +{Upgrade}" : "");

    public string DisplayName => Count > 1 ? $"{Name} ×{Count}" : Name;

    public Color RarityColor => Rarity switch
    {
        Rarity.Rare => new Color(0.45f, 0.7f, 1f),
        Rarity.Epic => new Color(0.8f, 0.5f, 1f),
        Rarity.Legendary => new Color(1f, 0.6f, 0.15f),
        _ => new Color(0.92f, 0.92f, 0.92f),
    };

    // «Редкость» — женского рода, поэтому прилагательные в женском роде подходят к любому предмету
    public string RarityName => Rarity switch
    {
        Rarity.Rare => "редкая",
        Rarity.Epic => "эпическая",
        Rarity.Legendary => "легендарная",
        _ => "обычная",
    };

    /// <summary>Все бонусы предмета: шаблон + свойства + небольшая прибавка эпическим вещам.</summary>
    public Stats Stats
    {
        get
        {
            var total = Template.Stats;
            foreach (var affix in Affixes)
                total += affix.Stats;
            if (Rarity == Rarity.Epic)
                total += Slot == EquipSlot.Weapon ? new Stats(Attack: 1) : new Stats(Defense: 1);
            return total + UpgradeStats(Slot, Upgrade);
        }
    }

    /// <summary>
    /// Прибавка от заточки. Защита растёт вдвое медленнее атаки: каждая её единица снимает
    /// единицу с каждого удара врага, и без этого броня делала бы героя неуязвимым.
    /// </summary>
    public static Stats UpgradeStats(EquipSlot slot, int level) => slot switch
    {
        EquipSlot.Weapon => new Stats(Attack: level),
        EquipSlot.Armor or EquipSlot.Helmet => new Stats(Defense: (level + 1) / 2, MaxHp: 3 * level),
        EquipSlot.Ring => new Stats(Crit: 5 * level),
        EquipSlot.Amulet => new Stats(MaxHp: 6 * level),
        _ => default,
    };

    /// <summary>Цена одной штуки у торговца.</summary>
    public int Price => Template.Price * Rarity switch { Rarity.Rare => 2, Rarity.Epic => 4, _ => 1 };

    public int SellPrice => Math.Max(1, Price / 2);

    public bool CanStackWith(Item other) => IsStackable && other.Template == Template && Count < MaxStack;

    /// <summary>Одна штука того же предмета (для продажи и выбрасывания из стопки).</summary>
    public Item CloneSingle() => new(Template, Rarity, Affixes);

    /// <summary>Короткое описание эффекта в одну строку.</summary>
    public string ShortEffect()
    {
        if (IsEquipment)
            return string.Join(", ", Stats.Describe());

        int power = Template.Power;
        return Kind switch
        {
            ItemKind.HealthPotion => $"+{power} здоровья",
            ItemKind.StrengthPotion => $"+{power} к атаке на {ItemCatalog.BuffDuration} ходов",
            ItemKind.InvisibilityPotion => $"враги не замечают тебя {power} ходов",
            ItemKind.TeleportScroll => "переносит в случайное место этажа",
            ItemKind.FireScroll => $"{power}+этаж урона всем видимым врагам",
            ItemKind.FreezeScroll => $"замораживает видимых врагов на {power} ходов",
            ItemKind.MapScroll => "открывает карту всего этажа",
            ItemKind.Bomb => $"{power}+этаж урона всем врагам в радиусе 2",
            _ => "",
        };
    }
}

/// <summary>Все шаблоны и свойства предметов в одном месте — легко менять баланс.</summary>
public static class ItemCatalog
{
    public const int BuffDuration = 20;

    private static readonly Color Steel = new(0.8f, 0.82f, 0.9f);
    private static readonly Color Leather = new(0.6f, 0.4f, 0.2f);
    private static readonly Color Iron = new(0.6f, 0.62f, 0.65f);
    private static readonly Color Gold = new(1f, 0.8f, 0.3f);

    public static readonly ItemTemplate Dagger = new("Кинжал", ItemKind.Weapon, new Stats(Attack: 2), 0, Steel, 1, 2, 10);
    public static readonly ItemTemplate Sword = new("Меч", ItemKind.Weapon, new Stats(Attack: 4), 0, Steel, 2, 6, 30);
    public static readonly ItemTemplate Axe = new("Топор", ItemKind.Weapon, new Stats(Attack: 6), 0, Steel, 4, 5, 55);
    public static readonly ItemTemplate Rapier = new("Рапира", ItemKind.Weapon, new Stats(Attack: 3, Crit: 15), 0, Steel, 3, 4, 45);

    public static readonly ItemTemplate LeatherArmor = new("Кожаная броня", ItemKind.Armor, new Stats(Defense: 1), 0, Leather, 1, 7, 15);
    public static readonly ItemTemplate ChainMail = new("Кольчуга", ItemKind.Armor, new Stats(Defense: 2), 0, Iron, 2, 5, 35);
    public static readonly ItemTemplate PlateArmor = new("Латы", ItemKind.Armor, new Stats(Defense: 4), 0, new Color(0.85f, 0.85f, 0.9f), 4, 3, 70);

    public static readonly ItemTemplate LeatherCap = new("Кожаный шлем", ItemKind.Helmet, new Stats(Defense: 1), 0, Leather, 1, 5, 12);
    public static readonly ItemTemplate IronHelmet = new("Железный шлем", ItemKind.Helmet, new Stats(Defense: 2), 0, Iron, 3, 4, 35);

    public static readonly ItemTemplate PowerRing = new("Кольцо силы", ItemKind.Ring, new Stats(Attack: 2), 0, Gold, 2, 3, 40);
    public static readonly ItemTemplate AccuracyRing = new("Кольцо меткости", ItemKind.Ring, new Stats(Crit: 15), 0, Gold, 2, 3, 40);
    public static readonly ItemTemplate RegenRing = new("Кольцо восстановления", ItemKind.Ring, new Stats(Regen: 2), 0, Gold, 3, 3, 45);

    public static readonly ItemTemplate LifeAmulet = new("Амулет жизни", ItemKind.Amulet, new Stats(MaxHp: 10), 0, new Color(1f, 0.4f, 0.4f), 2, 3, 40);
    public static readonly ItemTemplate SightAmulet = new("Амулет зоркости", ItemKind.Amulet, new Stats(Vision: 3), 0, new Color(0.4f, 0.8f, 1f), 1, 3, 30);
    public static readonly ItemTemplate VampireAmulet = new("Амулет крови", ItemKind.Amulet, new Stats(Lifesteal: 2), 0, new Color(0.7f, 0.1f, 0.2f), 3, 3, 50);

    public static readonly ItemTemplate HealthPotion = new("Зелье лечения", ItemKind.HealthPotion, default, 12, new Color(1f, 0.3f, 0.5f), 1, 30, 12);
    public static readonly ItemTemplate GreatHealthPotion = new("Большое зелье", ItemKind.HealthPotion, default, 25, new Color(1f, 0.15f, 0.3f), 3, 12, 25);
    public static readonly ItemTemplate StrengthPotion = new("Зелье силы", ItemKind.StrengthPotion, default, 3, new Color(1f, 0.55f, 0.1f), 2, 8, 20);
    public static readonly ItemTemplate InvisibilityPotion = new("Зелье невидимости", ItemKind.InvisibilityPotion, default, 12, new Color(0.7f, 0.9f, 0.95f), 2, 6, 25);
    public static readonly ItemTemplate TeleportScroll = new("Свиток телепорта", ItemKind.TeleportScroll, default, 0, new Color(0.4f, 0.6f, 1f), 1, 9, 15);
    public static readonly ItemTemplate FireScroll = new("Свиток огня", ItemKind.FireScroll, default, 8, new Color(1f, 0.45f, 0.1f), 2, 9, 25);
    public static readonly ItemTemplate FreezeScroll = new("Свиток холода", ItemKind.FreezeScroll, default, 5, new Color(0.5f, 0.85f, 1f), 2, 7, 22);
    public static readonly ItemTemplate MapScroll = new("Свиток карты", ItemKind.MapScroll, default, 0, new Color(0.85f, 0.8f, 0.5f), 1, 6, 15);
    public static readonly ItemTemplate Bomb = new("Бомба", ItemKind.Bomb, default, 10, new Color(0.3f, 0.3f, 0.35f), 2, 7, 20);

    // Легендарные вещи не выпадают обычным путём: их дают боссы, ларцы и алтари
    public static readonly ItemTemplate[] Legendaries =
    {
        new("Клинок бури", ItemKind.Weapon, new Stats(Attack: 6), 0, new Color(0.5f, 0.75f, 1f), 1, 0, 300, LegendaryEffect.Storm, "Каждая третья атака задевает всех врагов вокруг."),
        new("Кольцо алчности", ItemKind.Ring, new Stats(Crit: 5), 0, new Color(1f, 0.8f, 0.2f), 1, 0, 300, LegendaryEffect.Greed, "+1 к удару за каждые 100 золота (до +8)."),
        new("Амулет феникса", ItemKind.Amulet, new Stats(MaxHp: 10), 0, new Color(1f, 0.45f, 0.1f), 1, 0, 300, LegendaryEffect.Phoenix, "Раз за этаж спасает от смерти."),
        new("Плащ теней", ItemKind.Armor, new Stats(Defense: 3), 0, new Color(0.3f, 0.25f, 0.4f), 1, 0, 300, LegendaryEffect.Shadow, "После победы над врагом ты исчезаешь на два хода."),
    };

    public static Item RollLegendary(Random rng) => new(Legendaries[rng.Next(Legendaries.Length)], Rarity.Legendary);

    public static readonly ItemTemplate[] All =
    {
        Dagger, Sword, Axe, Rapier, LeatherArmor, ChainMail, PlateArmor, LeatherCap, IronHelmet,
        PowerRing, AccuracyRing, RegenRing, LifeAmulet, SightAmulet, VampireAmulet,
        HealthPotion, GreatHealthPotion, StrengthPotion, InvisibilityPotion,
        TeleportScroll, FireScroll, FreezeScroll, MapScroll, Bomb,
    };

    public static readonly Affix[] Affixes =
    {
        new("вампира", new Stats(Lifesteal: 2)),
        new("пламени", new Stats(FireDamage: 3)),
        new("точности", new Stats(Crit: 10)),
        new("шипов", new Stats(Thorns: 3)),
        new("жизни", new Stats(MaxHp: 8)),
        new("стойкости", new Stats(Defense: 1)),
        new("остроты", new Stats(Attack: 2)),
        new("зоркости", new Stats(Vision: 2)),
        new("исцеления", new Stats(Regen: 1)),
        // Проклятые свойства: сильный плюс и ощутимый минус, решай сам
        new("кровопийцы", new Stats(Attack: 4, MaxHp: -10)),
        new("безумия", new Stats(Crit: 25, Defense: -2)),
        new("слепца", new Stats(Attack: 3, Vision: -3)),
    };

    /// <summary>
    /// Случайный предмет для этажа. Сначала взвешенный выбор шаблона (представь отрезок,
    /// где каждый шаблон занимает длину Weight, и бросаем на него точку), потом редкость.
    /// </summary>
    public static Item Roll(Random rng, int floor)
    {
        var pool = All.Where(template => template.MinFloor <= floor).ToArray();
        int roll = rng.Next(pool.Sum(template => template.Weight));
        var chosen = pool[^1];
        foreach (var template in pool)
        {
            if (roll < template.Weight)
            {
                chosen = template;
                break;
            }
            roll -= template.Weight;
        }

        if (chosen.Slot == EquipSlot.None)
            return new Item(chosen);

        // Чем глубже, тем чаще редкие и эпические вещи
        double epicChance = Math.Min(0.03 + floor * 0.02, 0.2);
        double rareChance = Math.Min(0.15 + floor * 0.03, 0.45);
        double rarityRoll = rng.NextDouble();
        var rarity = rarityRoll < epicChance ? Rarity.Epic
            : rarityRoll < epicChance + rareChance ? Rarity.Rare
            : Rarity.Common;

        int affixCount = rarity switch { Rarity.Rare => 1, Rarity.Epic => 2, _ => 0 };
        var affixes = Affixes.OrderBy(_ => rng.Next()).Take(affixCount).ToArray(); // перемешали и взяли первые
        return new Item(chosen, rarity, affixes);
    }
}

/// <summary>
/// Подробное описание предмета для окон инвентаря и магазина — в разметке BBCode для RichTextLabel:
/// [color=#rrggbb]текст[/color] красит кусок текста. Бонусы сравнения — зелёные, потери — красные.
/// </summary>
public static class ItemInfo
{
    public static string Describe(Item item, Inventory inventory, string actionHint = null)
    {
        var text = new System.Text.StringBuilder();
        text.Append($"[font_size=20]{Colored(item.Name, item.RarityColor)}[/font_size]\n");
        string kind = item.IsEquipment ? Inventory.SlotName(item.Slot).ToLower() : "расходник";
        text.Append(Colored($"Редкость: {item.RarityName}  •  {kind}", UiTheme.TextMuted)).Append("\n\n");

        if (!item.IsEquipment)
        {
            text.Append(item.ShortEffect()).Append('\n');
        }
        else
        {
            foreach (var (_, line) in item.Stats.DescribeSigned())
                text.Append(line).Append('\n');
            if (item.Template.Special != null)
                text.Append(Colored(item.Template.Special, item.RarityColor)).Append('\n');

            // Сравнение с тем, что сейчас надето в этот же слот
            var equipped = inventory.GetEquipped(item.Slot);
            if (equipped == null)
            {
                text.Append('\n').Append(Colored("Слот пуст — всё это будет прибавкой.", UiTheme.Positive)).Append('\n');
            }
            else if (equipped != item)
            {
                text.Append('\n').Append(Colored($"Вместо «{equipped.Name}»:", UiTheme.TextMuted)).Append('\n');
                var difference = (item.Stats - equipped.Stats).DescribeSigned();
                if (difference.Count == 0)
                    text.Append(Colored("без изменений", UiTheme.TextMuted)).Append('\n');
                foreach (var (value, line) in difference)
                    text.Append(Colored(line, value > 0 ? UiTheme.Positive : UiTheme.Negative)).Append('\n');
            }
            else
            {
                text.Append('\n').Append(Colored("Сейчас надето", UiTheme.TextMuted)).Append('\n');
            }
        }

        if (actionHint != null)
            text.Append('\n').Append(Colored(actionHint, UiTheme.Accent));
        return text.ToString();
    }

    /// <summary>Что сделает Enter с этим предметом — подсказка внизу описания.</summary>
    public static string ActionHint(Item item) => item.Kind switch
    {
        _ when item.IsEquipment => "Enter — надеть",
        ItemKind.HealthPotion or ItemKind.StrengthPotion or ItemKind.InvisibilityPotion => "Enter — выпить",
        ItemKind.Bomb => "Enter — взорвать",
        _ => "Enter — прочитать",
    };

    private static string Colored(string text, Color color) => $"[color=#{color.ToHtml(false)}]{text}[/color]";
}
