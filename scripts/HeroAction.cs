using Godot;

namespace Roguelike;

public enum HeroActionKind
{
    Move,    // шаг в направлении Direction (атака, если там враг); Vector2I.Zero — пропустить ход
    Skill,   // способность номер Index (0 — F, 1 — G)
    UseItem, // предмет из ячейки сумки Index
}

/// <summary>
/// Действие героя, которое тратит ход. Нажатие клавиши у себя или (позже) сообщение по сети
/// от друга превращается в такой объект, а GameManager выполняет его одинаково для всех героев.
/// record struct — маленькая неизменяемая запись, её удобно передавать и сравнивать.
/// </summary>
public readonly record struct HeroAction(HeroActionKind Kind, Vector2I Direction = default, int Index = 0)
{
    public static HeroAction Move(Vector2I direction) => new(HeroActionKind.Move, direction);
    public static HeroAction Wait => new(HeroActionKind.Move, Vector2I.Zero);
    public static HeroAction Skill(int index) => new(HeroActionKind.Skill, Index: index);
    public static HeroAction UseItem(int index) => new(HeroActionKind.UseItem, Index: index);
}
