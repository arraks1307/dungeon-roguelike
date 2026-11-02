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
    Boss,   // Король орков: ближний бой + призыв помощников
    Necromancer, // держит дистанцию, стреляет и поднимает скелетов
    Overlord,    // финальный босс: всё сразу, на половине здоровья звереет
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
    private const int BossSummonInterval = 5;

    // Рост врагов с глубиной: +HpPerFloor здоровья за этаж и +1 атаки каждые AttackFloorStep этажей
    public const int HpPerFloor = 2;
    public const int AttackFloorStep = 3;

    public static readonly EnemyType[] Types =
    {
        new("Слизень", 6, 2, 0, new Color(0.4f, 0.85f, 0.3f), EnemyShape.Sphere, EnemyBehavior.Melee, Slow: true, MinFloor: 1),
        new("Гоблин", 10, 3, 0, new Color(0.85f, 0.7f, 0.25f), EnemyShape.Capsule, EnemyBehavior.Melee, Slow: false, MinFloor: 1),
        new("Лучник", 8, 3, 0, new Color(0.3f, 0.75f, 0.7f), EnemyShape.Cone, EnemyBehavior.Archer, Slow: false, MinFloor: 2),
        new("Призрак", 7, 4, 0, new Color(0.75f, 0.8f, 1f), EnemyShape.Capsule, EnemyBehavior.Ghost, Slow: true, MinFloor: 3),
        new("Орк", 18, 5, 1, new Color(0.85f, 0.25f, 0.2f), EnemyShape.Box, EnemyBehavior.Melee, Slow: false, MinFloor: 3),
        new("Скелет", 14, 5, 1, new Color(0.88f, 0.86f, 0.78f), EnemyShape.Capsule, EnemyBehavior.Melee, Slow: false, MinFloor: 11),
        // Мимик сам по этажам не бродит (MinFloor 99) — он появляется только из сундука
        new("Мимик", 20, 6, 1, new Color(0.55f, 0.33f, 0.14f), EnemyShape.Box, EnemyBehavior.Melee, Slow: false, MinFloor: 99),
    };

    public static EnemyType Mimic => Array.Find(Types, type => type.Name == "Мимик");
    public static EnemyType Skeleton => Array.Find(Types, type => type.Name == "Скелет");

    public static readonly EnemyType[] Bosses =
    {
        new("Король орков", 34, 5, 2, new Color(0.55f, 0.1f, 0.12f), EnemyShape.Box, EnemyBehavior.Boss, Slow: false, MinFloor: 5),
        new("Некромант", 60, 7, 1, new Color(0.35f, 0.15f, 0.45f), EnemyShape.Cone, EnemyBehavior.Necromancer, Slow: false, MinFloor: 10),
        new("Владыка бездны", 110, 9, 3, new Color(0.25f, 0.05f, 0.08f), EnemyShape.Box, EnemyBehavior.Overlord, Slow: false, MinFloor: 15),
    };

    /// <summary>Босс этажа: 5 — Король орков, 10 — Некромант, 15 — Владыка бездны.</summary>
    public static EnemyType BossForFloor(int floor) => Bosses[Math.Clamp(floor / 5 - 1, 0, Bosses.Length - 1)];

    public EnemyType Type { get; private set; }
    public bool IsBoss => Type.Behavior is EnemyBehavior.Boss or EnemyBehavior.Necromancer or EnemyBehavior.Overlord;
    public bool IsFinalBoss => Type.Behavior == EnemyBehavior.Overlord;

    /// <summary>Вторая фаза финального босса: меньше половины здоровья.</summary>
    private bool IsEnraged => IsFinalBoss && Hp <= MaxHp / 2;

    /// <summary>Заметил ли враг игрока. Пока не заметил — стоит на месте.</summary>
    public bool Alerted { get; set; }

    /// <summary>Призван боссом — за него не дают опыта, иначе на боссе можно было бы «фармить» уровни.</summary>
    public bool IsSummoned { get; set; }

    /// <summary>Опыт за победу над этим врагом.</summary>
    public int ExperienceReward => IsBoss ? 6 : IsSummoned ? 0 : 1;

    private static readonly Color IceColor = new(0.55f, 0.85f, 1f);

    /// <summary>Сколько ходов враг ещё заморожен (свиток холода).</summary>
    public int FrozenTurns { get; private set; }

    private bool _restingThisTurn;
    private int _turnsTaken;

    protected override Color DisplayColor => FrozenTurns > 0
        ? new Color(BaseColor.Lerp(IceColor, 0.7f), BaseColor.A)
        : BaseColor;

    public void Freeze(int turns)
    {
        FrozenTurns = turns;
        BodyMaterial.AlbedoColor = DisplayColor;
    }

    public static EnemyType FindType(string name) =>
        Array.Find(Types, type => type.Name == name) ?? Array.Find(Bosses, type => type.Name == name);

    public EnemySave CreateSave() => new()
    {
        Type = Type.Name,
        X = GridPos.X,
        Y = GridPos.Y,
        Hp = Hp,
        Frozen = FrozenTurns,
        Alerted = Alerted,
        Boss = IsBoss,
        Summoned = IsSummoned,
    };

    /// <summary>Вернуть состояние из сохранения (вызывается после Setup).</summary>
    public void RestoreState(EnemySave save)
    {
        SetHp(Math.Max(save.Hp, 1));
        Alerted = save.Alerted;
        IsSummoned = save.Summoned;
        if (save.Frozen > 0)
            Freeze(save.Frozen);
    }

    public void Setup(EnemyType type, int floor, DifficultyDef difficulty)
    {
        Type = type;
        DisplayName = type.Name;
        // С каждым этажом враги немного крепче, а сложность умножает здоровье и урон
        MaxHp = Mathf.RoundToInt((type.Hp + (floor - 1) * HpPerFloor) * difficulty.EnemyHp);
        Hp = MaxHp;
        Attack = Math.Max(1, Mathf.RoundToInt((type.Attack + (floor - 1) / AttackFloorStep) * difficulty.EnemyDamage));
        Defense = type.Defense;

        if (IsBoss)
        {
            CreateBossBody(type);
            CreateHealthBar(type.Behavior == EnemyBehavior.Overlord ? 2.1f : 1.75f);
            return;
        }

        CreateHealthBar(1.05f);

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

    /// <summary>У каждого босса свой силуэт, чтобы его сразу можно было узнать.</summary>
    private void CreateBossBody(EnemyType type)
    {
        var gold = Ui.TitleColor;
        switch (type.Behavior)
        {
            case EnemyBehavior.Necromancer:
                // Высокая мантия, череп и светящийся посох
                CreateBody(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.4f, Height = 1.3f }, type.Color, 0.65f);
                Body.AddChild(Glowing(new SphereMesh { Radius = 0.17f, Height = 0.34f }, new Color(0.9f, 0.88f, 0.8f), 0.2f, new Vector3(0f, 0.78f, 0f)));
                Body.AddChild(Glowing(new BoxMesh { Size = new Vector3(0.05f, 1.4f, 0.05f) }, new Color(0.3f, 0.2f, 0.1f), 0f, new Vector3(0.42f, 0.1f, 0f)));
                Body.AddChild(Glowing(new SphereMesh { Radius = 0.1f, Height = 0.2f }, new Color(0.6f, 1f, 0.5f), 2.5f, new Vector3(0.42f, 0.85f, 0f)));
                break;

            case EnemyBehavior.Overlord:
                // Огромная тёмная глыба с рогами и раскалёнными глазами
                CreateBody(new BoxMesh { Size = new Vector3(1.1f, 1.6f, 1f) }, type.Color, 0.8f);
                var red = new Color(1f, 0.15f, 0.05f);
                Body.AddChild(Glowing(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.12f, Height = 0.45f }, new Color(0.15f, 0.12f, 0.1f), 0f, new Vector3(-0.35f, 1f, 0f)));
                Body.AddChild(Glowing(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.12f, Height = 0.45f }, new Color(0.15f, 0.12f, 0.1f), 0f, new Vector3(0.35f, 1f, 0f)));
                Body.AddChild(Glowing(new SphereMesh { Radius = 0.08f, Height = 0.16f }, red, 4f, new Vector3(-0.22f, 0.45f, 0.5f)));
                Body.AddChild(Glowing(new SphereMesh { Radius = 0.08f, Height = 0.16f }, red, 4f, new Vector3(0.22f, 0.45f, 0.5f)));
                AddChild(new OmniLight3D { LightColor = red, LightEnergy = 1.5f, OmniRange = 4f, Position = new Vector3(0f, 1.2f, 0f) });
                break;

            default:
                // Король орков: здоровяк в золотой короне
                CreateBody(new BoxMesh { Size = new Vector3(0.9f, 1.3f, 0.9f) }, type.Color, 0.65f);
                Body.AddChild(Glowing(new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.3f, Height = 0.2f }, gold, 0.6f, new Vector3(0f, 0.75f, 0f)));
                break;
        }
    }

    private static MeshInstance3D Glowing(PrimitiveMesh mesh, Color color, float glow, Vector3 position)
    {
        var material = new StandardMaterial3D { AlbedoColor = color };
        if (glow > 0f)
        {
            material.EmissionEnabled = true;
            material.Emission = color;
            material.EmissionEnergyMultiplier = glow;
        }
        mesh.Material = material;
        return new MeshInstance3D { Mesh = mesh, Position = position };
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
        if (FrozenTurns > 0)
        {
            if (--FrozenTurns == 0)
                BodyMaterial.AlbedoColor = DisplayColor; // оттаял
            return;
        }

        if (world.PlayerInvisible)
        {
            Alerted = false; // теряет игрока из виду и стоит на месте
            return;
        }

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
                world.SummonMinions(this, 2, null);
                break;
            case EnemyBehavior.Necromancer when _turnsTaken % BossSummonInterval == 0:
                world.SummonMinions(this, 2, Skeleton);
                break;
            case EnemyBehavior.Necromancer:
                ArcherTurn(world, stepsToPlayer); // в остальное время ведёт себя как лучник
                break;
            case EnemyBehavior.Overlord:
                OverlordTurn(world, stepsToPlayer);
                break;
            default:
                MeleeTurn(world);
                break;
        }
    }

    private bool _enraged;

    /// <summary>
    /// Финальный босс. Первая фаза: ближний бой, огненные залпы через ход, призыв раз в 6 ходов.
    /// Вторая фаза (меньше половины здоровья): +3 к атаке и призыв раз в 3 хода.
    /// </summary>
    private void OverlordTurn(IGameWorld world, int stepsToPlayer)
    {
        if (IsEnraged && !_enraged)
        {
            _enraged = true;
            Attack += 3;
            world.Announce($"{DisplayName} впадает в ярость!");
        }

        int summonInterval = _enraged ? 3 : 6;
        if (_turnsTaken % summonInterval == 0)
        {
            world.SummonMinions(this, 2, _turnsTaken % 2 == 0 ? Skeleton : Array.Find(Types, t => t.Name == "Орк"));
            return;
        }
        if (stepsToPlayer is >= 2 and <= 5 && world.IsVisibleToPlayer(GridPos) && _turnsTaken % 2 == 0)
        {
            world.EnemyAttacksPlayer(this, ranged: true);
            return;
        }
        MeleeTurn(world);
    }

    private void MeleeTurn(IGameWorld world)
    {
        if (world.Map.AreAdjacent(GridPos, world.PlayerPos))
        {
            world.EnemyAttacksPlayer(this, ranged: false);
            return;
        }
        var step = ChooseStep(world.PlayerDistances, cell => world.Map.CanMove(GridPos, cell) && !world.IsOccupied(cell));
        if (!step.HasValue)
            return;
        // Закрытую дверь на пути враг сначала открывает — это занимает его ход
        if (world.Map.Get(step.Value) == Tile.Door)
            world.OpenDoor(step.Value);
        else
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
