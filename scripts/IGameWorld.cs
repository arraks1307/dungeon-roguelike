using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// То, что врагу нужно знать о мире и что он может в нём сделать.
/// GameManager реализует этот интерфейс, а Enemy зависит только от него,
/// а не от всего GameManager целиком.
/// </summary>
public interface IGameWorld
{
    DungeonMap Map { get; }

    /// <summary>
    /// Ближайший герой, которого враг может преследовать: живой и не невидимый.
    /// null — таких нет (все погибли или скрылись).
    /// </summary>
    Player NearestTarget(Vector2I from);

    /// <summary>Сколько шагов от каждой клетки до ближайшего героя-цели (считается раз за ход).</summary>
    Dictionary<Vector2I, int> PlayerDistances { get; }

    bool IsOccupied(Vector2I cell);

    /// <summary>Видит ли эту клетку отряд (обзор у героев общий).</summary>
    bool IsVisibleToPlayer(Vector2I cell);

    void EnemyAttacksPlayer(Enemy enemy, Player target, bool ranged, float power = 1f);
    void EnemyTelegraphs(Enemy enemy, string text);
    void EnemyHeavyHit(Enemy enemy, Vector2I cell, float power);
    void EnemyExplodes(Enemy enemy, float power);
    void EnemySlam(Enemy enemy, int radius, float power);
    Enemy FindHurtAlly(Enemy healer, int range);
    void ShamanHeals(Enemy shaman, Enemy ally);
    void OpenDoor(Vector2I cell);

    /// <summary>Призвать помощников рядом с собой. type = null — случайные слабые враги.</summary>
    void SummonMinions(Enemy summoner, int count, EnemyType type);

    /// <summary>Важное сообщение в журнал (например, ярость босса).</summary>
    void Announce(string text);
}
