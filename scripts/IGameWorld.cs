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
    Vector2I PlayerPos { get; }

    /// <summary>Сколько шагов от каждой клетки до игрока (поиск в ширину, считается раз за ход).</summary>
    Dictionary<Vector2I, int> PlayerDistances { get; }

    bool IsOccupied(Vector2I cell);
    bool IsVisibleToPlayer(Vector2I cell);

    void EnemyAttacksPlayer(Enemy enemy, bool ranged);
    void SummonMinions(Enemy summoner, int count);
}
