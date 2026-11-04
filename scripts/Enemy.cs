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
    Brute,       // замахивается на клетку, через ход сильно бьёт: можно отойти
    Bomber,      // рядом с героем поджигает фитиль и через ход взрывается
    Shieldbearer, // чередует глухую защиту и удар
    Shaman,      // лечит раненых врагов, а без дела стреляет издалека
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
        new("Подрывник", 6, 4, 0, new Color(1f, 0.5f, 0.1f), EnemyShape.Sphere, EnemyBehavior.Bomber, Slow: false, MinFloor: 2),
        new("Щитоносец", 14, 4, 1, new Color(0.55f, 0.6f, 0.75f), EnemyShape.Box, EnemyBehavior.Shieldbearer, Slow: false, MinFloor: 3),
        new("Громила", 24, 6, 1, new Color(0.5f, 0.35f, 0.6f), EnemyShape.Box, EnemyBehavior.Brute, Slow: false, MinFloor: 4),
        new("Шаман", 12, 3, 0, new Color(0.3f, 0.9f, 0.6f), EnemyShape.Cone, EnemyBehavior.Shaman, Slow: false, MinFloor: 5),
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

    /// <summary>Что враг умеет — для подсказки при наведении и бестиария.</summary>
    public static string Describe(EnemyType type) => type.Behavior switch
    {
        EnemyBehavior.Archer => "Стреляет издалека и держит дистанцию.",
        EnemyBehavior.Ghost => "Летает сквозь стены и чует героев.",
        EnemyBehavior.Boss => "Босс: зовёт помощников и бьёт по площади.",
        EnemyBehavior.Necromancer => "Босс: держит дистанцию и поднимает скелетов.",
        EnemyBehavior.Overlord => "Финальный босс: на половине здоровья впадает в ярость.",
        EnemyBehavior.Brute => "Замахивается на клетку: отойди или встань в стойку.",
        EnemyBehavior.Bomber => "Рядом поджигает фитиль и взрывается: убей или отбеги.",
        EnemyBehavior.Shieldbearer => "Щит держит удары. Бей после его атаки, с фланга или магией.",
        EnemyBehavior.Shaman => "Лечит других врагов: убей первым.",
        _ => type.Slow ? "Медленный: ходит через ход." : "Идёт к цели и бьёт вплотную.",
    };

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
    public int ExperienceReward => IsBoss ? 6 : IsSummoned ? 0 : IsElite ? 2 : 1;

    /// <summary>Элитный враг: крупнее, крепче и сильнее обычного, опыта даёт вдвое больше.</summary>
    public bool IsElite { get; private set; }

    /// <summary>Щит поднят: удары оружием и стрелы почти не проходят, а магия проходит.</summary>
    public bool ShieldUp { get; private set; }

    private const float HeavyHitPower = 2.2f;
    private const float ExplosionPower = 3f;
    private const int ShamanRange = 6;
    private const int SlamInterval = 4;
    private const int SlamRadius = 2;
    private const float SlamPower = 1.6f;

    private Vector2I? _heavyCell;   // куда Громила обрушит удар на следующем ходу
    private bool _fuseLit;          // Подрывник взорвётся на следующем ходу
    private bool _slamReady;        // босс ударит по площади на следующем ходу
    private MeshInstance3D _warning;
    private MeshInstance3D _shield;

    public void MakeElite(float hpScale)
    {
        IsElite = true;
        DisplayName = $"Элитный {Type.Name.ToLower()}";
        MaxHp = Mathf.RoundToInt(MaxHp * hpScale);
        SetHp(MaxHp);
        Attack += 1 + Attack / 4;
        Scale = Vector3.One * 1.25f;
    }

    public void RestoreHp(int amount) => SetHp(Hp + amount);

    private static readonly Color IceColor = new(0.55f, 0.85f, 1f);

    /// <summary>Сколько ходов враг ещё заморожен (свиток холода).</summary>
    public int FrozenTurns { get; private set; }

    private bool _restingThisTurn;
    private int _turnsTaken;

    protected override Color DisplayColor => FrozenTurns > 0
        ? new Color(BaseColor.Lerp(IceColor, 0.7f), BaseColor.A)
        : BaseColor;

    /// <summary>Кто заморозил (номер героя, -1 — никто): союзник этого героя разобьёт врага двойным уроном.</summary>
    public int FrozenBy { get; private set; } = -1;

    public void Freeze(int turns, int by = -1)
    {
        FrozenTurns = turns;
        FrozenBy = by;
        CancelThreat(); // мороз сбивает замах и гасит фитиль

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
        Elite = IsElite,
    };

    /// <summary>Вернуть состояние из сохранения (вызывается после Setup).</summary>
    public void RestoreState(EnemySave save, float eliteHp)
    {
        if (save.Elite)
            MakeElite(eliteHp);
        SetHp(Math.Max(save.Hp, 1));
        Alerted = save.Alerted;
        IsSummoned = save.Summoned;
        if (save.Frozen > 0)
            Freeze(save.Frozen);
    }

    /// <summary>
    /// partyHpScale — множитель здоровья для отряда из нескольких героев: вчетвером
    /// бить врагов проще, поэтому они крепче (урон не растёт — иначе одиночки погибали бы мгновенно).
    /// </summary>
    public void Setup(EnemyType type, int floor, DifficultyDef difficulty, float partyHpScale = 1f, float partyDamageScale = 1f)
    {
        Type = type;
        DisplayName = type.Name;
        // С каждым этажом враги немного крепче, а сложность и размер отряда умножают здоровье
        MaxHp = Mathf.RoundToInt((type.Hp + (floor - 1) * HpPerFloor) * difficulty.EnemyHp * partyHpScale);
        Hp = MaxHp;
        Attack = Math.Max(1, Mathf.RoundToInt((type.Attack + (floor - 1) / AttackFloorStep) * difficulty.EnemyDamage * partyDamageScale));
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

        AddEyes(type);
        if (type.Behavior == EnemyBehavior.Ghost)
            MakeTranslucent(0.45f);
        if (type.Behavior == EnemyBehavior.Shieldbearer)
            CreateShield();
    }

    private void CreateShield()
    {
        _shield = Part(new BoxMesh { Size = new Vector3(0.62f, 0.72f, 0.08f) }, new Color(0.8f, 0.85f, 0.95f), new Vector3(0f, 0.02f, 0.4f), glow: 0.3f);
        Body.AddChild(_shield); // на теле: поворачивается вместе с ним
        SetShield(true);
    }

    private void SetShield(bool up)
    {
        ShieldUp = up;
        if (_shield != null)
            _shield.Visible = up;
    }

    /// <summary>Красное кольцо на полу: сюда придётся удар. Стоит, пока угроза не сработает.</summary>
    private void ShowWarning(Vector2I cell, float size)
    {
        ClearWarning();
        _warning = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = 0.36f * size,
                OuterRadius = 0.46f * size,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.15f, 0.1f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.15f, 0.1f),
                    EmissionEnergyMultiplier = 2.5f,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
            TopLevel = true, // кольцо стоит на месте, даже если враг шевелится
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_warning);
        _warning.GlobalPosition = GridToWorld(cell) + new Vector3(0f, 0.06f, 0f);
    }

    private void ClearWarning()
    {
        _warning?.QueueFree();
        _warning = null;
    }

    private void CancelThreat()
    {
        _heavyCell = null;
        _fuseLit = false;
        _slamReady = false;
        ClearWarning();
    }

    /// <summary>
    /// Светящиеся глаза на «лицевой» стороне (+Z — сторона, обращённая к камере).
    /// Даже простая фигура с глазами читается как существо, а в темноте глаза видны первыми.
    /// </summary>
    private void AddEyes(EnemyType type)
    {
        // Положение глаз относительно центра тела зависит от формы: (половина расстояния между глазами, высота, глубина)
        var (spread, height, depth) = type.Shape switch
        {
            EnemyShape.Sphere => (0.09f, 0.06f, 0.26f),
            EnemyShape.Box => (0.13f, 0.2f, 0.31f),
            EnemyShape.Cone => (0.05f, 0.12f, 0.16f),
            _ => (0.07f, 0.2f, 0.18f),
        };
        var color = type.Name switch
        {
            "Орк" or "Мимик" => new Color(1f, 0.35f, 0.2f),
            "Призрак" => new Color(0.55f, 0.9f, 1f),
            "Скелет" => new Color(0.4f, 1f, 0.6f),
            _ => new Color(1f, 0.95f, 0.75f),
        };
        foreach (float side in new[] { -1f, 1f })
            Body.AddChild(Part(new SphereMesh { Radius = 0.04f, Height = 0.08f }, color, new Vector3(side * spread, height, depth), glow: 2.5f));
    }

    /// <summary>У каждого босса свой силуэт, чтобы его сразу можно было узнать.</summary>
    private void CreateBossBody(EnemyType type)
    {
        var gold = new Color(1f, 0.8f, 0.3f);
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

        // Цель — ближайший живой герой, которого не скрывает невидимость
        var target = world.NearestTarget(GridPos);
        if (target == null)
        {
            Alerted = false; // некого преследовать — теряет след и стоит на месте
            return;
        }

        int stepsToTarget = StepsBetween(GridPos, target.GridPos);
        if (world.IsVisibleToPlayer(GridPos))
            Alerted = true;
        if (Type.Behavior == EnemyBehavior.Ghost && stepsToTarget <= GhostSenseRadius)
            Alerted = true; // призрак чувствует героев сквозь стены

        if (!Alerted || !ShouldActThisTurn())
            return;
        _turnsTaken++;

        if (IsBoss && BossSlamTurn(world, stepsToTarget))
            return;

        switch (Type.Behavior)
        {
            case EnemyBehavior.Archer:
                ArcherTurn(world, target, stepsToTarget);
                break;
            case EnemyBehavior.Ghost:
                GhostTurn(world, target, stepsToTarget);
                break;
            case EnemyBehavior.Boss when _turnsTaken % BossSummonInterval == 0:
                world.SummonMinions(this, 2, null);
                break;
            case EnemyBehavior.Necromancer when _turnsTaken % BossSummonInterval == 0:
                world.SummonMinions(this, 2, Skeleton);
                break;
            case EnemyBehavior.Necromancer:
                ArcherTurn(world, target, stepsToTarget); // в остальное время ведёт себя как лучник
                break;
            case EnemyBehavior.Overlord:
                OverlordTurn(world, target, stepsToTarget);
                break;
            case EnemyBehavior.Brute:
                BruteTurn(world, target);
                break;
            case EnemyBehavior.Bomber:
                BomberTurn(world, target);
                break;
            case EnemyBehavior.Shieldbearer:
                ShieldbearerTurn(world, target);
                break;
            case EnemyBehavior.Shaman:
                ShamanTurn(world, target, stepsToTarget);
                break;
            default:
                MeleeTurn(world, target);
                break;
        }
    }

    /// <summary>Босс бьёт по площади: кольцо появляется заранее, и отряд должен разбежаться.</summary>
    private bool BossSlamTurn(IGameWorld world, int stepsToTarget)
    {
        if (_slamReady)
        {
            CancelThreat();
            world.EnemySlam(this, SlamRadius, SlamPower);
            return true;
        }
        if (_turnsTaken % SlamInterval != 2 || stepsToTarget > SlamRadius)
            return false;
        _slamReady = true;
        ShowWarning(GridPos, 5.5f);
        world.EnemyTelegraphs(this, $"{DisplayName} поднимает оружие над головой!");
        return true;
    }

    /// <summary>Замах и удар через ход. Герой видит клетку и успевает уйти или встать в стойку.</summary>
    private void BruteTurn(IGameWorld world, Player target)
    {
        if (_heavyCell.HasValue)
        {
            var cell = _heavyCell.Value;
            CancelThreat();
            world.EnemyHeavyHit(this, cell, HeavyHitPower);
            return;
        }
        if (world.Map.AreAdjacent(GridPos, target.GridPos))
        {
            _heavyCell = target.GridPos;
            ShowWarning(target.GridPos, 1f);
            world.EnemyTelegraphs(this, $"{DisplayName} заносит оружие!");
            return;
        }
        MeleeTurn(world, target);
    }

    /// <summary>Рядом с героем поджигает фитиль. Следующий ход — взрыв вокруг себя.</summary>
    private void BomberTurn(IGameWorld world, Player target)
    {
        if (_fuseLit)
        {
            CancelThreat();
            world.EnemyExplodes(this, ExplosionPower);
            return;
        }
        if (world.Map.AreAdjacent(GridPos, target.GridPos))
        {
            _fuseLit = true;
            ShowWarning(GridPos, 3f);
            world.EnemyTelegraphs(this, $"{DisplayName} поджигает фитиль!");
            return;
        }
        MeleeTurn(world, target);
    }

    /// <summary>Рядом с героем чередует удар и глухую защиту: бить надо сразу после его атаки.</summary>
    private void ShieldbearerTurn(IGameWorld world, Player target)
    {
        if (!world.Map.AreAdjacent(GridPos, target.GridPos))
        {
            SetShield(true);
            MeleeTurn(world, target);
            return;
        }
        if (ShieldUp)
        {
            SetShield(false);
            world.EnemyAttacksPlayer(this, target, ranged: false);
            return;
        }
        SetShield(true);
    }

    private void ShamanTurn(IGameWorld world, Player target, int stepsToTarget)
    {
        var ally = world.FindHurtAlly(this, ShamanRange);
        if (ally != null)
            world.ShamanHeals(this, ally);
        else
            ArcherTurn(world, target, stepsToTarget);
    }

    private bool _enraged;

    /// <summary>
    /// Финальный босс. Первая фаза: ближний бой, огненные залпы через ход, призыв раз в 6 ходов.
    /// Вторая фаза (меньше половины здоровья): +3 к атаке и призыв раз в 3 хода.
    /// </summary>
    private void OverlordTurn(IGameWorld world, Player target, int stepsToTarget)
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
        if (stepsToTarget is >= 2 and <= 5 && world.IsVisibleToPlayer(GridPos) && _turnsTaken % 2 == 0)
        {
            world.EnemyAttacksPlayer(this, target, ranged: true);
            return;
        }
        MeleeTurn(world, target);
    }

    private void MeleeTurn(IGameWorld world, Player target)
    {
        if (world.Map.AreAdjacent(GridPos, target.GridPos))
        {
            world.EnemyAttacksPlayer(this, target, ranged: false);
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

    private void ArcherTurn(IGameWorld world, Player target, int stepsToTarget)
    {
        bool canSeeTarget = world.IsVisibleToPlayer(GridPos);

        if (canSeeTarget && stepsToTarget == 1)
        {
            // Герой вплотную: пытаемся отступить, а если некуда — бьём луком в ближнем бою
            var retreat = FindRetreatStep(world, target);
            if (retreat.HasValue)
                MoveTo(retreat.Value);
            else
                world.EnemyAttacksPlayer(this, target, ranged: false);
            return;
        }

        if (canSeeTarget && stepsToTarget <= ArcherRange)
        {
            world.EnemyAttacksPlayer(this, target, ranged: true);
            return;
        }

        MeleeTurn(world, target); // не видит или далеко — подходит ближе
    }

    private Vector2I? FindRetreatStep(IGameWorld world, Player target)
    {
        foreach (var direction in DungeonMap.Directions)
        {
            var next = GridPos + direction;
            if (StepsBetween(next, target.GridPos) > 1 && world.Map.CanMove(GridPos, next) && !world.IsOccupied(next))
                return next;
        }
        return null;
    }

    /// <summary>
    /// Призрак летит напрямую к цели сквозь стены. Атаковать он может только
    /// из клетки пола — иначе герой не смог бы ударить его в ответ.
    /// </summary>
    private void GhostTurn(IGameWorld world, Player target, int stepsToTarget)
    {
        if (stepsToTarget == 1 && world.Map.IsWalkable(GridPos))
        {
            world.EnemyAttacksPlayer(this, target, ranged: false);
            return;
        }

        int bestScore = GhostScore(world, target, GridPos);
        Vector2I? best = null;
        foreach (var direction in DungeonMap.Directions)
        {
            var next = GridPos + direction;
            if (!world.Map.InBounds(next) || world.IsOccupied(next))
                continue;
            int score = GhostScore(world, target, next);
            if (score < bestScore)
            {
                bestScore = score;
                best = next;
            }
        }
        if (best.HasValue)
            MoveTo(best.Value);
    }

    // Чем меньше, тем лучше: главное — расстояние до цели, при равенстве лучше клетка пола, а не стена
    private static int GhostScore(IGameWorld world, Player target, Vector2I cell) =>
        StepsBetween(cell, target.GridPos) * 2 + (world.Map.IsWalkable(cell) ? 0 : 1);

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
