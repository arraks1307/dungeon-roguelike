using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

public enum EnemyShape
{
    Sphere,
    Capsule,
    Box,
}

/// <summary>
/// Описание вида врага. record — это класс-«запись» с готовым конструктором,
/// удобен для неизменяемых данных.
/// </summary>
public record EnemyType(string Name, int Hp, int Attack, Color Color, EnemyShape Shape, bool Slow, int MinFloor);

public partial class Enemy : Actor
{
    public static readonly EnemyType[] Types =
    {
        new("Слизень", 6, 2, new Color(0.4f, 0.85f, 0.3f), EnemyShape.Sphere, Slow: true, MinFloor: 1),
        new("Гоблин", 10, 3, new Color(0.85f, 0.7f, 0.25f), EnemyShape.Capsule, Slow: false, MinFloor: 1),
        new("Орк", 18, 5, new Color(0.85f, 0.25f, 0.2f), EnemyShape.Box, Slow: false, MinFloor: 3),
    };

    public EnemyType Type { get; private set; }

    /// <summary>Заметил ли враг игрока. Пока не заметил — стоит на месте.</summary>
    public bool Alerted { get; set; }

    private bool _restingThisTurn;

    public void Setup(EnemyType type, int floor)
    {
        Type = type;
        DisplayName = type.Name;
        // С каждым этажом враги немного крепче
        MaxHp = type.Hp + (floor - 1) * 2;
        Hp = MaxHp;
        Attack = type.Attack + (floor - 1) / 2;

        switch (type.Shape)
        {
            case EnemyShape.Sphere:
                CreateBody(new SphereMesh { Radius = 0.3f, Height = 0.45f }, type.Color, 0.22f);
                break;
            case EnemyShape.Capsule:
                CreateBody(new CapsuleMesh { Radius = 0.2f, Height = 0.7f }, type.Color, 0.35f);
                break;
            case EnemyShape.Box:
                CreateBody(new BoxMesh { Size = new Vector3(0.6f, 0.8f, 0.6f) }, type.Color, 0.4f);
                break;
        }
    }

    /// <summary>Медленные враги действуют через ход.</summary>
    public bool ShouldActThisTurn()
    {
        if (!Type.Slow)
            return true;
        _restingThisTurn = !_restingThisTurn;
        return !_restingThisTurn;
    }

    /// <summary>
    /// Выбираем соседнюю клетку, которая ближе к игроку по полю расстояний.
    /// canEnter проверяет, можно ли туда шагнуть (нет стены, не занято другим).
    /// Возвращает null, если подходящего шага нет (всё занято или путь не найден).
    /// </summary>
    public Vector2I? ChooseStep(Dictionary<Vector2I, int> distances, Func<Vector2I, bool> canEnter)
    {
        if (!distances.TryGetValue(GridPos, out int bestDistance))
            return null;

        Vector2I? best = null;
        foreach (var dir in DungeonMap.Directions)
        {
            var next = GridPos + dir;
            if (distances.TryGetValue(next, out int distance) && distance < bestDistance && canEnter(next))
            {
                bestDistance = distance;
                best = next;
            }
        }
        return best;
    }
}
