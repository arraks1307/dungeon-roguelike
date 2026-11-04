using Godot;

namespace Roguelike;

public enum HeroActionKind
{
    // Действия, которые тратят ход
    Move,    // шаг в направлении Direction (атака, если там враг); Vector2I.Zero — пропустить ход
    Skill,   // способность номер Index (0 — F, 1 — G)
    UseItem, // предмет из ячейки сумки Index

    // «Свободные» действия: меняют мир, но ход не тратят
    Drop,    // выбросить предмет из ячейки Index
    Buy,     // купить товар Index; Extra — сколько товаров видел покупатель (защита от «купил не то»)
    Sell,    // продать предмет из ячейки Index
    Perk,    // выбрать улучшение номер Index из ClassCatalog.Perks
    Upgrade, // услуга: заточить надетую вещь из слота Index (EquipSlot)
    Treat,   // услуга: вылечиться у торговца
    Mystery, // услуга: купить таинственный ларец
    GiveItem, // передать союзнику предмет из ячейки Index
    GiveGold, // передать союзнику золото
    Leave,   // игрок отключился: герой ждёт его возвращения (присылает только хост)
    Rejoin,  // игрок вернулся (присылает только хост)
}

/// <summary>
/// Действие героя. Нажатие клавиши у себя, автопилот или сообщение по сети от друга превращается
/// в такой объект, а GameManager выполняет его одинаково для всех героев и на всех компьютерах.
/// record struct — маленькая неизменяемая запись, её удобно передавать и сравнивать.
/// </summary>
public readonly record struct HeroAction(HeroActionKind Kind, Vector2I Direction = default, int Index = 0, int Extra = 0)
{
    public static HeroAction Move(Vector2I direction) => new(HeroActionKind.Move, direction);
    public static HeroAction Wait => new(HeroActionKind.Move, Vector2I.Zero);
    public static HeroAction Skill(int index) => new(HeroActionKind.Skill, Index: index);
    public static HeroAction UseItem(int index) => new(HeroActionKind.UseItem, Index: index);
    public static HeroAction Drop(int index) => new(HeroActionKind.Drop, Index: index);
    public static HeroAction Buy(int index, int stockCount) => new(HeroActionKind.Buy, Index: index, Extra: stockCount);
    public static HeroAction Sell(int index) => new(HeroActionKind.Sell, Index: index);
    public static HeroAction Perk(int index) => new(HeroActionKind.Perk, Index: index);
    public static HeroAction Upgrade(int slot) => new(HeroActionKind.Upgrade, Index: slot);
    public static HeroAction Treat => new(HeroActionKind.Treat);
    public static HeroAction Mystery => new(HeroActionKind.Mystery);
    public static HeroAction GiveItem(int index) => new(HeroActionKind.GiveItem, Index: index);
    public static HeroAction GiveGold => new(HeroActionKind.GiveGold);
    public static HeroAction Leave => new(HeroActionKind.Leave);
    public static HeroAction Rejoin => new(HeroActionKind.Rejoin);

    /// <summary>Тратит ли действие ход (иначе выполняется сразу и раунд не двигает).</summary>
    public bool SpendsTurn => Kind is HeroActionKind.Move or HeroActionKind.Skill or HeroActionKind.UseItem;
}
