using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

public enum EnemyShape
{
    Sphere,
    Capsule,
    Box,
    Cone,
}

public enum EnemyBehavior
{
    Melee,  // идёт к игроку и бьёт вплотную
    Archer, // стреляет издалека и держит дистанцию
    Ghost,  // проходит сквозь стены
    Boss,   // ближний бой + призыв помощников
}

/// <summary>
/// Описание вида врага. record — это класс-«запись» с готовым конструктором,
/// удобен для неизменяемых данных.
/// </summary>
public record EnemyType(
    string Name, int Hp, int Attack, int Defense, Color Color, EnemyShape Shape,
    EnemyBehavior Behavior, bool Slow, int MinFloor);

public partial class Enemy : Actor
{
    private const int ArcherRange = 6;
    private const int GhostSenseRadius = 6;
    private const int BossSummonInterval = 4;

    public static readonly EnemyType[] Types =
    {
        new("Слизень", 6, 2, 0, new Color(0.4f, 0.85f, 0.3f), EnemyShape.Sphere, EnemyBehavior.Melee, Slow: true, MinFloor: 1),
        new("Гоблин", 10, 3, 0, new Color(0.85f, 0.7f, 0.25f), EnemyShape.Capsule, EnemyBehavior.Melee, Slow: false, MinFloor: 1),
        new("Лучник", 8, 3, 0, new Color(0.3f, 0.75f, 0.7f), EnemyShape.Cone, EnemyBehavior.Archer, Slow: false, MinFloor: 2),
        new("Призрак", 7, 4, 0, new Color(0.75f, 0.8f, 1f), EnemyShape.Capsule, EnemyBehavior.Ghost, Slow: true, MinFloor: 3),
        new("Орк", 18, 5, 1, new Color(0.85f, 0.25f, 0.2f), EnemyShape.Box, EnemyBehavior.Melee, Slow: false, MinFloor: 3),
    };

    public static readonly EnemyType BossType =
        new("Король орков", 50, 6, 2, new Color(0.55f, 0.1f, 0.12f), EnemyShape.Box, EnemyBehavior.Boss, Slow: false, MinFloor: 5);

    public EnemyType Type { get; private set; }
    public bool IsBoss => Type.Behavior == EnemyBehavior.Boss;

    /// <summary>Заметил ли враг игрока. Пока не заметил — стоит на месте.</summary>
    public bool Alerted { get; set; }

    private bool _restingThisTurn;
    private int _turnsTaken;

    public void Setup(EnemyType type, int floor)
    {
        Type = type;
        DisplayName = type.Name;
        // С каждым этажом враги немного крепче
        MaxHp = type.Hp + (floor - 1) * 2;
        Hp = MaxHp;
        Attack = type.Attack + (floor - 1) / 2;
        Defense = type.Defense;

        if (IsBoss)
        {
            CreateBody(new BoxMesh { Size = new Vector3(0.9f, 1.3f, 0.9f) }, type.Color, 0.65f);
            // Корона: золотое кольцо на голове
            Body.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.35f, BottomRadius = 0.3f, Height = 0.2f,
                    Material = new StandardMaterial3D
                    {
                        AlbedoColor = Ui.TitleColor, EmissionEnabled = true, Emission = Ui.TitleColor,
                        EmissionEnergyMultiplier = 0.6f,
                    },
                },
                Position = new Vector3(0f, 0.75f, 0f),
            });
            return;
        }

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
            case EnemyShape.Cone:
                CreateBody(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.28f, Height = 0.8f }, type.Color, 0.4f);
                break;
        }

        if (type.Behavior == EnemyBehavior.Ghost)
            MakeTranslucent(0.45f);
    }

    /// <summary>Медленные враги действуют через ход.</summary>
    private bool ShouldActThisTurn()
    {
        if (!Type.Slow)
            return true;
        _restingThisTurn = !_restingThisTurn;
        return !_restingThisTurn;
    }

    /// <summary>Расстояние Чебышёва: сколько шагов (с диагоналями) до клетки, если нет препятствий.</summary>
    private static int StepsBetween(Vector2I a, Vector2I b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // ---------- Ход врага ----------

    public void TakeTurn(IGameWorld world)
    {
        int stepsToPlayer = StepsBetween(GridPos, world.PlayerPos);
        if (world.IsVisibleToPlayer(GridPos))
            Alerted = true;
        if (Type.Behavior == EnemyBehavior.Ghost && stepsToPlayer <= GhostSenseRadius)
            Alerted = true; // призрак чувствует игрока сквозь стены

        if (!Alerted || !ShouldActThisTurn())
            return;
        _turnsTaken++;

        switch (Type.Behavior)
        {
            case EnemyBehavior.Archer:
                ArcherTurn(world, stepsToPlayer);
                break;
            case EnemyBehavior.Ghost:
                GhostTurn(world, stepsToPlayer);
                break;
            case EnemyBehavior.Boss when _turnsTaken % BossSummonInterval == 0:
                world.SummonMinions(this, 2);
                break;
            default:
                MeleeTurn(world);
                break;
        }
    }

    private void MeleeTurn(IGameWorld world)
    {
        if (world.Map.AreAdjacent(GridPos, world.PlayerPos))
        {
            world.EnemyAttacksPlayer(this, ranged: false);
            return;
        }
        var step = ChooseStep(world.PlayerDistances, cell => world.Map.CanMove(GridPos, cell) && !world.IsOccupied(cell));
        if (step.HasValue)
            MoveTo(step.Value);
    }

    private void ArcherTurn(IGameWorld world, int stepsToPlayer)
    {
        bool canSeePlayer = world.IsVisibleToPlayer(GridPos);

        if (canSeePlayer && stepsToPlayer == 1)
        {
            // Игрок вплотную: пытаемся отступить, а если некуда — бьём луком в ближнем бою
            var retreat = FindRetreatStep(world);
            if (retreat.HasValue)
                MoveTo(retreat.Value);
            else
                world.EnemyAttacksPlayer(this, ranged: false);
            return;
        }

        if (canSeePlayer && stepsToPlayer <= ArcherRange)
        {
            world.EnemyAttacksPlayer(this, ranged: true);
            return;
        }

        MeleeTurn(world); // не видит или далеко — подходит ближе
    }

    private Vector2I? FindRetreatStep(IGameWorld world)
    {
        foreach (var direction in DungeonMap.Directions)
        {
            var next = GridPos + direction;
            if (StepsBetween(next, world.PlayerPos) > 1 && world.Map.CanMove(GridPos, next) && !world.IsOccupied(next))
                return next;
        }
        return null;
    }

    /// <summary>
    /// Призрак летит напрямую к игроку сквозь стены. Атаковать он может только
    /// из клетки пола — иначе игрок не смог бы ударить его в ответ.
    /// </summary>
    private void GhostTurn(IGameWorld world, int stepsToPlayer)
    {
        if (stepsToPlayer == 1 && world.Map.IsWalkable(GridPos))
        {
            world.EnemyAttacksPlayer(this, ranged: false);
            return;
        }

        int bestScore = GhostScore(world, GridPos);
        Vector2I? best = null;
        foreach (var direction in DungeonMap.Directions)
        {
            var next = GridPos + direction;
            if (!world.Map.InBounds(next) || world.IsOccupied(next))
                continue;
            int score = GhostScore(world, next);
            if (score < bestScore)
            {
                bestScore = score;
                best = next;
            }
        }
        if (best.HasValue)
            MoveTo(best.Value);
    }

    // Чем меньше, тем лучше: главное — расстояние до игрока, при равенстве лучше клетка пола, а не стена
    private static int GhostScore(IGameWorld world, Vector2I cell) =>
        StepsBetween(cell, world.PlayerPos) * 2 + (world.Map.IsWalkable(cell) ? 0 : 1);

    /// <summary>
    /// Выбираем соседнюю клетку, которая ближе к игроку по полю расстояний.
    /// canEnter проверяет, можно ли туда шагнуть (нет стены, не занято другим).
    /// Возвращает null, если подходящего шага нет (всё занято или путь не найден).
    /// </summary>
    private Vector2I? ChooseStep(Dictionary<Vector2I, int> distances, Func<Vector2I, bool> canEnter)
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
