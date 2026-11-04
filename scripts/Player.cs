using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static Roguelike.Shapes;

namespace Roguelike;

/// <summary>
/// Игрок: читает клавиши движения и сообщает GameManager, что хочет сделать.
/// Хранит класс, характеристики, инвентарь, золото, улучшения, перезарядку способностей
/// и временные эффекты зелий. Остальные клавиши обрабатывает GameManager.
/// </summary>
public partial class Player : Actor
{
    private const int RegenInterval = 8; // регенерация срабатывает раз в столько ходов
    private const float InvisibleAlpha = 0.3f;

    // Умная ходьба: сколько держать клавишу до начала автоходьбы и пауза между её шагами (секунды)
    private const double HoldDelay = 0.3;
    private const double WalkInterval = 0.15;

    protected override void ApplyFacing(float angle)
    {
        base.ApplyFacing(angle);
        if (_gear != null && _gear.GetParent() != Body)
            _gear.Rotation = new Vector3(0f, angle, 0f);
    }

    protected override bool ContinuousMove => _heldKey != Key.None && !_walkStopped;

    /// <summary>Нажатие клавиши: шаг, атака или (Vector2I.Zero) пропуск хода. Одно нажатие — одно действие.</summary>
    public event Action<Vector2I> ActionRequested;

    /// <summary>
    /// Клавиша удерживается — просьба сделать ещё один шаг. GameManager выполняет его, только если это
    /// безопасное перемещение, и останавливает ходьбу через StopWalking, когда происходит что-то важное.
    /// </summary>
    public event Action<Vector2I> WalkRequested;

    private Key _heldKey = Key.None;
    private Vector2I _heldDirection;
    private double _holdTimer;
    private bool _walkStopped;

    // ----- Участник отряда -----

    /// <summary>Номер героя в отряде (0 — хост или одиночный игрок). По нему же сеть узнаёт, чей это герой.</summary>
    public int Id { get; set; }

    /// <summary>Героем управляют с этого компьютера: он слушает клавиатуру, за ним следит камера и HUD.</summary>
    public bool IsLocal { get; set; } = true;

    /// <summary>Героем управляет автопилот (только для самопроверки и симуляций).</summary>
    public bool IsBot { get; set; }

    /// <summary>Уже действовал в этом раунде — ждёт, пока сходят остальные, потом ходят враги.</summary>
    public bool HasActed { get; set; }

    /// <summary>Защитная стойка: герой пропустил ход и держит оборону до своего следующего действия.</summary>
    public bool IsGuarding { get; set; }

    // Отряд: подъём союзника, силы призрака и итоги забега
    public int ReviveProgress { get; set; }

    public int StormCounter { get; set; }
    public bool PhoenixUsed { get; set; }
    public bool HasLegendary(LegendaryEffect effect) => Inventory.Equipped.Any(item => item.Template.Effect == effect);

    /// <summary>Игрок потерял связь: герой стоит на месте и ждёт, враги его не трогают.</summary>
    public bool IsOffline { get; set; }
    public int GhostReadyRound { get; set; }
    public int DamageDealt { get; set; }
    public int KillsMade { get; set; }
    public int RevivesMade { get; set; }
    public int BiggestHit { get; set; }
    public int GoldSpent { get; set; }
    public int PotionsDrunk { get; set; }
    public int LavaSteps { get; set; }

    private readonly List<Pact> _pacts = new();

    /// <summary>Сделки с подземельем: действуют до конца забега.</summary>
    public IReadOnlyList<Pact> Pacts => _pacts;

    public bool HasPact(PactRule rule) => _pacts.Any(pact => pact.Rule == rule);

    /// <summary>Постоянные бонусы героя мира: очки и черта, выбранные при создании.</summary>
    public void SetBuild(Stats build)
    {
        _build = build;
        RecalculateStats();
    }

    private Stats _build;

    public void AddPact(Pact pact)
    {
        _pacts.Add(pact);
        RecalculateStats();
    }

    /// <summary>Сколько улучшений этот герой ещё должен выбрать (за раунд можно получить несколько уровней).</summary>
    public int PendingLevelUps { get; set; }

    /// <summary>Погибший герой — призрак до следующего этажа: не ходит, враги его не замечают.</summary>
    public bool IsGhost => IsDead;

    public void SetDisplayName(string name) => DisplayName = name;

    private Label3D _nameplate;

    /// <summary>
    /// Табличка с именем над героем — для союзников в отряде. Label3D — текст в 3D-мире;
    /// Billboard поворачивает его к камере, NoDepthTest не даёт стенам его закрывать.
    /// </summary>
    public void UpdateNameplate(bool show)
    {
        if (!show)
        {
            if (_nameplate != null)
                _nameplate.Visible = false;
            return;
        }
        _nameplate ??= CreateNameplate();
        _nameplate.Visible = true;
        _nameplate.Text = IsOffline ? $"{DisplayName} (нет связи)" : IsGhost ? $"{DisplayName} (призрак)" : DisplayName;
        var color = IsGhost ? new Color(GhostColor, 0.8f) : Class.Color.Lightened(0.45f);
        _nameplate.Modulate = color;
    }

    private Label3D CreateNameplate()
    {
        var label = new Label3D
        {
            Position = new Vector3(0f, 1.35f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 40,
            PixelSize = 0.006f,
            OutlineSize = 12,
            OutlineModulate = new Color(0f, 0f, 0f, 0.8f),
        };
        AddChild(label);
        return label;
    }

    /// <summary>Отряд спустился на новый этаж — призрак оживает с половиной здоровья.</summary>
    public void Revive(float share = 0.5f)
    {
        Hp = Math.Max(1, (int)(MaxHp * share));
        ReviveProgress = 0;
        BodyMaterial.AlbedoColor = DisplayColor;
    }

    public ClassDef Class { get; private set; } = ClassCatalog.Warrior;
    public int Level { get; private set; }
    public int Experience { get; private set; }

    /// <summary>
    /// Сколько опыта нужно до следующего уровня. Растёт с уровнем, чтобы к концу игры герой
    /// не превращался в неуязвимую машину (это показали симуляции).
    /// </summary>
    public int ExperienceToNextLevel => 1 + Level;
    public int Gold { get; set; }
    public Inventory Inventory { get; } = new();

    /// <summary>Сумма бонусов надетой экипировки и выбранных улучшений.</summary>
    public Stats Bonuses { get; private set; }

    /// <summary>Сколько ходов осталось до готовности каждой из двух способностей (0 — готова).</summary>
    public int[] SkillCooldowns { get; } = new int[2];

    public int StrengthTurns { get; private set; }
    public int InvisibleTurns { get; private set; }
    public bool IsInvisible => InvisibleTurns > 0;
    public bool IsHurt => Hp < MaxHp;

    // Базовые значения без экипировки: зависят от класса и растут с уровнем
    private int _baseAttack;
    private int _baseMaxHp;
    private int _strengthBonus;
    private int _turnCounter;
    private readonly List<Perk> _perks = new();

    private static readonly Color GhostColor = new(0.75f, 0.85f, 1f, 0.3f);

    protected override Color DisplayColor =>
        IsGhost ? GhostColor
        : IsInvisible ? new Color(BaseColor, InvisibleAlpha)
        : BaseColor;

    public override void _Ready()
    {
        DisplayName = "Ты";
        CreateBody(new CapsuleMesh { Radius = 0.19f, Height = 0.56f }, Class.Color, 0.52f);

        // «Факел» — тёплый точечный свет, который двигается вместе с игроком
        // Свет повыше над головой — иначе голова пересвечена. Сам герой тень от него не отбрасывает
        // (CastShadow = Off ниже), иначе под ним лежал бы тёмный круг; враги и стены — отбрасывают
        _torchLight = new OmniLight3D
        {
            Position = new Vector3(0f, 2.4f, 0f),
            LightColor = new Color(1f, 0.75f, 0.45f),
            LightEnergy = 2.8f,
            OmniRange = 11f,
            ShadowEnabled = true,
        };
        Body.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_torchLight);

        _dust = CreateDust();
        AddChild(_dust);

        ResetStats(ClassCatalog.Warrior);
        SetHighQuality(Settings.HighQuality);
    }

    private OmniLight3D _torchLight;
    private CpuParticles3D _dust;
    private Node3D _gear;
    private float _shownFade = -1f;

    /// <summary>На низком качестве — без теней от факела и без пылинок.</summary>
    public void SetTorch(bool enabled) => _torchLight.Visible = enabled;

    public void SetHighQuality(bool high)
    {
        // Тени от точечного света дорогие (их рендер — шесть направлений сразу), поэтому
        // тени даёт только факел своего героя; факелы союзников просто светят
        _torchLight.ShadowEnabled = high && IsLocal;
        // Пылинки — только вокруг своего героя, иначе вчетвером их было бы слишком много
        _dust.Emitting = high && IsLocal;
        _dust.Visible = high && IsLocal;
    }

    /// <summary>
    /// Пылинки, медленно парящие вокруг героя. Материал обычный (не светящийся), поэтому пылинки
    /// видны только там, куда падает свет факела, — как пыль в луче света.
    /// LocalCoords = false: родившись, пылинка остаётся на месте в мире, а не «едет» за героем.
    /// </summary>
    private static CpuParticles3D CreateDust() => new()
    {
        Amount = 45,
        Lifetime = 7f,
        Preprocess = 7f, // при появлении сразу «заполняем» воздух, а не ждём 7 секунд
        LocalCoords = false,
        EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
        EmissionBoxExtents = new Vector3(5f, 0.8f, 5f),
        Position = new Vector3(0f, 1.1f, 0f),
        Direction = Vector3.Up,
        Spread = 180f,
        Gravity = Vector3.Zero,
        InitialVelocityMin = 0.03f,
        InitialVelocityMax = 0.12f,
        ScaleAmountMin = 0.5f,
        ScaleAmountMax = 1.2f,
        Mesh = new QuadMesh
        {
            Size = new Vector2(0.035f, 0.035f),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.93f, 0.8f, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            },
        },
    };

    /// <summary>
    /// Герой собран как фигурка на шарнирах: руки и ноги качаются при ходьбе (см. Actor), а рука с оружием
    /// делает замах при ударе. Детали цвета класса идут через Flesh: у них общий материал с туловищем.
    /// Лицо смотрит в сторону +Z. Ноги стоят на полу: тело на высоте 0.52, бёдра на 0.32.
    /// </summary>
    private void BuildGear()
    {
        _gear?.QueueFree();
        ClearLimbs();
        _gear = new Node3D();
        Body.AddChild(_gear);
        _shownFade = -1f;

        var skin = new Color(0.95f, 0.8f, 0.68f);
        var dark = Class.Color.Darkened(0.45f);
        var light = Class.Color.Lightened(0.35f);
        var steel = new Color(0.78f, 0.8f, 0.86f);
        var wood = new Color(0.45f, 0.28f, 0.14f);
        var leather = new Color(0.34f, 0.22f, 0.13f);
        var gold = new Color(1f, 0.8f, 0.3f);
        var eye = new Color(0.1f, 0.08f, 0.08f);

        void Add(Node3D parent, PrimitiveMesh mesh, Color color, float x, float y, float z, Vector3? turn = null, float glow = 0f, float metal = 0f) =>
            parent.AddChild(Piece(mesh, color, new Vector3(x, y, z), turn, glow, metal));

        void Head()
        {
            Add(_gear, Ball(0.15f), skin, 0f, 0.4f, 0.03f);
            foreach (float side in new[] { -1f, 1f })
                Add(_gear, Ball(0.022f), eye, side * 0.06f, 0.42f, 0.165f);
        }

        void Belt(float radius) => Add(_gear, Rod(radius, 0.05f), leather, 0f, -0.14f, 0f);

        switch (Class.Class)
        {
            case PlayerClass.Warrior:
            {
                AddLegs(_gear, -0.2f, 0.095f, 0.3f, 0.075f, dark, leather);
                var (sword, shield) = AddArms(_gear, 0.17f, 0.25f, 0.3f, 0.06f, null, steel);
                Head();
                Add(_gear, Taper(0.2f, 0.225f, 0.32f), steel, 0f, 0.04f, 0f, metal: 0.6f);
                Add(_gear, Cube(0.2f, 0.46f, 0.03f), Class.Color, 0f, -0.08f, 0.22f);
                Belt(0.23f);
                Add(_gear, Ball(0.03f, 0.7f), gold, 0f, -0.14f, 0.235f, metal: 0.7f);
                Add(_gear, Ball(0.17f, 0.72f), steel, 0f, 0.47f, 0.02f, metal: 0.6f);
                Add(_gear, Ring(0.165f, 0.018f), gold, 0f, 0.43f, 0.02f, metal: 0.7f);
                Add(_gear, Cube(0.03f, 0.16f, 0.02f), steel, 0f, 0.42f, 0.19f, metal: 0.6f);
                Add(_gear, Cube(0.04f, 0.1f, 0.3f), Class.Color, 0f, 0.62f, -0.02f);
                foreach (float side in new[] { -1f, 1f })
                    Add(_gear, Ball(0.11f, 0.72f), steel, side * 0.25f, 0.22f, 0f, metal: 0.6f);
                Cape = AddCape(_gear, new Vector3(0f, 0.25f, -0.18f), 0.42f, 0.66f, dark);

                Add(sword, Cube(0.055f, 0.02f, 0.58f), steel, 0f, -0.3f, 0.36f, metal: 0.7f);
                Add(sword, Cube(0.2f, 0.035f, 0.035f), gold, 0f, -0.3f, 0.06f, metal: 0.7f);
                Add(sword, Rod(0.016f, 0.13f), leather, 0f, -0.3f, 0f, new Vector3(90f, 0f, 0f));
                Add(shield, Rod(0.21f, 0.035f), dark, -0.03f, -0.24f, 0.12f, new Vector3(90f, 0f, 0f), metal: 0.3f);
                Add(shield, Ring(0.205f, 0.016f), steel, -0.03f, -0.24f, 0.14f, new Vector3(90f, 0f, 0f), metal: 0.6f);
                Add(shield, Cube(0.05f, 0.3f, 0.02f), light, -0.03f, -0.24f, 0.14f);
                Add(shield, Cube(0.3f, 0.05f, 0.02f), light, -0.03f, -0.24f, 0.14f);
                Add(shield, Ball(0.05f, 0.6f), gold, -0.03f, -0.24f, 0.16f, metal: 0.7f);
                break;
            }

            case PlayerClass.Ranger:
            {
                AddLegs(_gear, -0.2f, 0.09f, 0.3f, 0.07f, leather, leather.Darkened(0.3f));
                var (_, bowArm) = AddArms(_gear, 0.17f, 0.23f, 0.3f, 0.055f, null, skin, weaponRest: -0.3f);
                Head();
                Add(_gear, Ball(0.175f), dark, 0f, 0.43f, -0.04f);
                Add(_gear, Cone(0.09f, 0.22f), dark, 0f, 0.52f, -0.18f, new Vector3(-60f, 0f, 0f));
                Add(_gear, Ring(0.17f, 0.04f), dark, 0f, 0.28f, 0f);
                Add(_gear, Taper(0.195f, 0.22f, 0.3f), leather, 0f, 0.0f, 0f);
                Add(_gear, Cube(0.05f, 0.55f, 0.02f), leather.Darkened(0.3f), 0f, 0.06f, 0.2f, new Vector3(0f, 0f, 35f));
                Belt(0.225f);
                Add(_gear, Cube(0.09f, 0.08f, 0.05f), leather.Lightened(0.15f), 0.15f, -0.17f, 0.15f);
                Add(_gear, Rod(0.06f, 0.42f), leather.Darkened(0.2f), -0.11f, 0.1f, -0.22f, new Vector3(14f, 0f, 0f));
                foreach (float x in new[] { -0.15f, -0.11f, -0.07f })
                {
                    Add(_gear, Rod(0.007f, 0.1f), wood, x, 0.34f, -0.17f);
                    Add(_gear, Cube(0.03f, 0.07f, 0.008f), light, x, 0.4f, -0.16f);
                }
                Cape = AddCape(_gear, new Vector3(0f, 0.25f, -0.17f), 0.34f, 0.5f, dark);

                bowArm.AddChild(Arc(wood, new Vector3(0f, -0.3f, 0.12f), 0.32f, 0.03f, 58f, rotationDegrees: new Vector3(0f, 0f, 28f)));
                Add(bowArm, Cube(0.56f, 0.007f, 0.007f), new Color(0.9f, 0.88f, 0.8f), 0f, -0.3f, -0.05f, new Vector3(0f, 0f, 28f));
                Add(bowArm, Rod(0.022f, 0.09f), leather, 0f, -0.3f, 0.12f, new Vector3(0f, 0f, 118f));
                break;
            }

            case PlayerClass.Rogue:
            {
                AddLegs(_gear, -0.2f, 0.09f, 0.3f, 0.065f, dark.Darkened(0.3f), Colors.Black.Lightened(0.12f));
                var (blade, offBlade) = AddArms(_gear, 0.17f, 0.23f, 0.3f, 0.052f, null, dark, weaponRest: -0.6f);
                Head();
                Add(_gear, Ball(0.17f), dark, 0f, 0.43f, -0.04f);
                Add(_gear, Cube(0.25f, 0.08f, 0.1f), dark.Darkened(0.2f), 0f, 0.35f, 0.12f);
                Add(_gear, Taper(0.19f, 0.21f, 0.3f), dark, 0f, 0f, 0f);
                Add(_gear, Cube(0.04f, 0.5f, 0.02f), leather, 0f, 0.05f, 0.2f, new Vector3(0f, 0f, -35f));
                Belt(0.215f);
                foreach (float x in new[] { -0.15f, 0.15f })
                    Add(_gear, Cube(0.07f, 0.08f, 0.05f), leather.Lightened(0.15f), x, -0.17f, 0.15f);
                Cape = AddCape(_gear, new Vector3(0.05f, 0.27f, -0.15f), 0.13f, 0.6f, Class.Color.Lightened(0.15f));

                foreach (var hand in new[] { blade, offBlade })
                {
                    Add(hand, Cube(0.04f, 0.015f, 0.34f), steel, 0f, -0.3f, 0.21f, metal: 0.7f);
                    Add(hand, Cube(0.12f, 0.025f, 0.025f), gold, 0f, -0.3f, 0.04f, metal: 0.7f);
                }
                break;
            }

            case PlayerClass.Priest:
            {
                var (staff, _) = AddArms(_gear, 0.17f, 0.24f, 0.3f, 0.06f, null, skin, weaponRest: -0.3f);
                Head();
                _gear.AddChild(Flesh(Taper(0.2f, 0.33f, 0.6f), new Vector3(0f, -0.22f, 0f)));
                Add(_gear, Ball(0.158f, 0.62f), new Color(0.72f, 0.52f, 0.3f), 0f, 0.47f, 0f);
                Add(_gear, Ring(0.16f, 0.04f), light, 0f, 0.27f, 0f);
                Add(_gear, Cube(0.13f, 0.82f, 0.02f), new Color(0.95f, 0.93f, 0.88f), 0f, -0.12f, 0.25f, new Vector3(-10f, 0f, 0f));
                Add(_gear, Ring(0.32f, 0.018f), gold, 0f, -0.49f, 0f, metal: 0.6f);
                Add(_gear, Rod(0.235f, 0.05f), gold, 0f, -0.08f, 0f, metal: 0.6f);
                Add(_gear, Ring(0.18f, 0.018f), gold, 0f, 0.66f, 0f, glow: 2.5f);

                Add(staff, Rod(0.022f, 1.15f), wood, 0f, -0.2f, 0.02f, new Vector3(16f, 0f, 0f));
                Add(staff, Cube(0.05f, 0.34f, 0.05f), gold, 0f, 0.44f, 0.2f, new Vector3(16f, 0f, 0f), glow: 0.8f, metal: 0.7f);
                Add(staff, Cube(0.24f, 0.05f, 0.05f), gold, 0f, 0.47f, 0.21f, glow: 0.8f, metal: 0.7f);
                break;
            }

            case PlayerClass.Berserk:
            {
                AddLegs(_gear, -0.2f, 0.1f, 0.3f, 0.08f, leather, leather.Darkened(0.3f));
                var (axe, _) = AddArms(_gear, 0.17f, 0.27f, 0.32f, 0.075f, null, skin);
                Head();
                Add(_gear, Taper(0.205f, 0.2f, 0.3f), skin, 0f, 0.04f, 0f);
                foreach (float side in new[] { -1f, 1f })
                {
                    Add(_gear, Cube(0.04f, 0.2f, 0.012f), Class.Color, side * 0.08f, 0.1f, 0.2f, new Vector3(0f, 0f, side * 25f));
                    Add(_gear, Ball(0.13f, 0.75f), new Color(0.42f, 0.3f, 0.2f), side * 0.25f, 0.23f, 0f);
                    Add(_gear, Cone(0.05f, 0.24f), new Color(0.9f, 0.86f, 0.75f), side * 0.2f, 0.62f, 0.02f, new Vector3(0f, 0f, side * -35f));
                }
                Add(_gear, Ball(0.165f, 0.65f), new Color(0.5f, 0.3f, 0.18f), 0f, 0.47f, 0.01f);
                Add(_gear, Rod(0.24f, 0.28f), new Color(0.42f, 0.3f, 0.2f), 0f, -0.22f, 0f);
                Belt(0.245f);

                Add(axe, Rod(0.024f, 0.78f), wood, 0f, -0.32f, 0.26f, new Vector3(90f, 0f, 0f));
                foreach (float side in new[] { -1f, 1f })
                    Add(axe, Taper(0.02f, 0.18f, 0.18f), steel, side * 0.13f, -0.32f, 0.54f, new Vector3(0f, 0f, side * -90f), metal: 0.6f);
                break;
            }

            case PlayerClass.Mage:
            {
                var (staff, _) = AddArms(_gear, 0.17f, 0.24f, 0.3f, 0.06f, null, skin, weaponRest: -0.3f);
                Head();
                _gear.AddChild(Flesh(Taper(0.2f, 0.34f, 0.6f), new Vector3(0f, -0.22f, 0f)));
                Add(_gear, Cone(0.1f, 0.24f), new Color(0.9f, 0.9f, 0.92f), 0f, 0.28f, 0.14f, new Vector3(190f, 0f, 0f));
                Add(_gear, Rod(0.25f, 0.025f), dark, 0f, 0.51f, 0f);
                Add(_gear, Taper(0.1f, 0.16f, 0.24f), dark, 0f, 0.63f, -0.01f);
                Add(_gear, Rod(0.165f, 0.04f), gold, 0f, 0.54f, 0f, metal: 0.6f);
                Add(_gear, Cone(0.1f, 0.26f), dark, 0f, 0.84f, -0.09f, new Vector3(-38f, 0f, 0f));
                Add(_gear, Ring(0.17f, 0.04f), dark, 0f, 0.26f, 0f);
                Add(_gear, Rod(0.235f, 0.05f), gold, 0f, -0.08f, 0f, metal: 0.6f);
                Add(_gear, Cube(0.15f, 0.2f, 0.05f), new Color(0.45f, 0.15f, 0.15f), -0.16f, -0.2f, 0.16f, new Vector3(0f, 0f, 8f));
                Add(_gear, Cube(0.12f, 0.17f, 0.052f), new Color(0.92f, 0.88f, 0.78f), -0.16f, -0.2f, 0.158f, new Vector3(0f, 0f, 8f));

                Add(staff, Rod(0.024f, 1.2f), wood, 0f, -0.2f, 0.02f, new Vector3(16f, 0f, 0f));
                staff.AddChild(Arc(gold, new Vector3(0f, 0.44f, 0.27f), 0.1f, 0.016f, 130f, rotationDegrees: new Vector3(90f, 0f, 0f)));
                Add(staff, Gem(0.075f), light, 0f, 0.47f, 0.2f, glow: 3f);
                break;
            }
        }

        foreach (var mesh in ModelMeshes())
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    /// <summary>Призрак и невидимка просвечивают целиком: и тело, и снаряжение.</summary>
    private void UpdateFade()
    {
        float fade = IsGhost ? 0.55f : IsInvisible ? 1f - InvisibleAlpha : 0f;
        if (Mathf.IsEqualApprox(fade, _shownFade) || Body == null)
            return;
        _shownFade = fade;
        foreach (var mesh in ModelMeshes())
            mesh.Transparency = fade;
    }

    public void ResetStats(ClassDef playerClass)
    {
        SetClass(playerClass);
        Level = 1;
        Experience = 0;
        Gold = 0;
        _baseAttack = playerClass.Attack;
        _baseMaxHp = playerClass.MaxHp;
        StrengthTurns = 0;
        InvisibleTurns = 0;
        _turnCounter = 0;
        _perks.Clear();
        _pacts.Clear();
        DamageDealt = KillsMade = RevivesMade = BiggestHit = GoldSpent = PotionsDrunk = LavaSteps = 0;
        Array.Clear(SkillCooldowns);
        BodyMaterial.AlbedoColor = DisplayColor;

        // Стартовый набор класса: экипировку сразу надеваем, остальное кладём в сумку
        Inventory.Clear();
        foreach (var template in playerClass.StartingItems)
        {
            Inventory.TryAdd(new Item(template));
            var added = Inventory.Items[^1];
            if (added.IsEquipment && Inventory.GetEquipped(added.Slot) == null)
                Inventory.Equip(Inventory.Items.Count - 1);
        }

        RecalculateStats();
        Hp = MaxHp;
    }

    private void SetClass(ClassDef playerClass)
    {
        Class = playerClass;
        BaseColor = playerClass.Color;
        BuildGear();
    }

    /// <summary>Добавить опыт. Возвращает, сколько уровней получено (за один раз может быть больше одного).</summary>
    public int GainExperience(int amount)
    {
        Experience += amount;
        int levels = 0;
        while (Experience >= ExperienceToNextLevel)
        {
            Experience -= ExperienceToNextLevel;
            LevelUp();
            levels++;
        }
        return levels;
    }

    /// <summary>Повышение уровня: небольшой рост характеристик. Главное — улучшение на выбор, его применяет ApplyPerk.</summary>
    private void LevelUp()
    {
        Level++;
        _baseMaxHp += 4;
        _baseAttack++;
        RecalculateStats();
        Heal(4);
    }

    public void ApplyPerk(Perk perk)
    {
        _perks.Add(perk);
        int before = MaxHp;
        RecalculateStats();
        Heal(MaxHp - before); // выросло максимальное здоровье — текущее растёт вместе с ним
    }

    public int CooldownReduction => _perks.Sum(perk => perk.CooldownReduction) + (HasPact(PactRule.Haste) ? 1 : 0);

    public bool Has(PerkEffect effect) => _perks.Any(perk => perk.Effect == effect);

    public IEnumerable<Perk> Perks => _perks;

    /// <summary>Постоянная прибавка к атаке (алтарь крови). Хранится в базовой атаке, поэтому попадает в сохранение.</summary>
    public void AddPermanentHp(int amount)
    {
        _baseMaxHp += amount;
        RecalculateStats();
    }

    public void AddPermanentAttack(int amount)
    {
        _baseAttack += amount;
        RecalculateStats();
    }

    /// <summary>
    /// Герой атаковал: способности заряжаются на одну атаку. except — способность, которой он
    /// только что ударил: сама себя она не заряжает.
    /// </summary>
    public void ChargeSkills(int except = -1)
    {
        for (int i = 0; i < SkillCooldowns.Length; i++)
            if (i != except && SkillCooldowns[i] > 0)
                SkillCooldowns[i] = Math.Max(0, SkillCooldowns[i] - (HasLegendary(LegendaryEffect.Echo) ? 2 : 1));
    }

    public void StartCooldown(int skillIndex) =>
        SkillCooldowns[skillIndex] = Class.Skills[skillIndex].Cooldown == 0
            ? 0 // способность без зарядки (меткий выстрел)
            : Math.Max(1, Class.Skills[skillIndex].Cooldown - CooldownReduction);

    public void Heal(int amount) => Hp = Mathf.Min(Hp + amount, MaxHp);

    public void Equip(int index)
    {
        Inventory.Equip(index);
        RecalculateStats();
    }

    public void ApplyStrength(int bonus)
    {
        _strengthBonus = bonus;
        StrengthTurns = ItemCatalog.BuffDuration;
        RecalculateStats();
    }

    public void ApplyInvisibility(int turns)
    {
        InvisibleTurns = turns;
        BodyMaterial.AlbedoColor = DisplayColor;
    }

    public void BreakInvisibility()
    {
        InvisibleTurns = 0;
        BodyMaterial.AlbedoColor = DisplayColor;
    }

    /// <summary>
    /// Вызывается в конце каждого хода: отсчитывает эффекты зелий и регенерацию.
    /// Возвращает, сколько здоровья восстановилось и какие эффекты закончились.
    /// </summary>
    public (int Healed, bool StrengthEnded, bool InvisibilityEnded) TickEffects()
    {
        _turnCounter++;

        int healed = 0;
        if (Bonuses.Regen > 0 && IsHurt && _turnCounter % RegenInterval == 0)
        {
            int before = Hp;
            Heal(Bonuses.Regen);
            healed = Hp - before;
        }

        bool strengthEnded = StrengthTurns > 0 && --StrengthTurns == 0;
        if (strengthEnded)
            RecalculateStats();

        bool invisibilityEnded = InvisibleTurns > 0 && --InvisibleTurns == 0;
        if (invisibilityEnded)
            BodyMaterial.AlbedoColor = DisplayColor;

        return (healed, strengthEnded, invisibilityEnded);
    }

    public PlayerSave CreateSave() => new()
    {
        X = GridPos.X,
        Y = GridPos.Y,
        Class = Class.Class,
        Level = Level,
        Experience = Experience,
        Hp = Hp,
        Gold = Gold,
        BaseAttack = _baseAttack,
        BaseMaxHp = _baseMaxHp,
        StrengthTurns = StrengthTurns,
        StrengthBonus = _strengthBonus,
        InvisibleTurns = InvisibleTurns,
        TurnCounter = _turnCounter,
        Cooldowns = SkillCooldowns.ToArray(),
        Perks = _perks.Select(perk => perk.Name).ToList(),
        Items = Inventory.Items.Select(ItemSave.From).ToList(),
        Equipped = Inventory.Equipped.Select(ItemSave.From).ToList(),
        Pacts = _pacts.Select(pact => pact.Name).ToList(),
        DamageDealt = DamageDealt, KillsMade = KillsMade, BiggestHit = BiggestHit,
        GoldSpent = GoldSpent, PotionsDrunk = PotionsDrunk, LavaSteps = LavaSteps,
    };

    /// <summary>allowDead — сохранить и нулевое здоровье (призрак в сетевом снимке мира).</summary>
    public void RestoreSave(PlayerSave save, bool allowDead = false)
    {
        SetClass(ClassCatalog.Get(save.Class));
        Level = save.Level;
        Experience = save.Experience;
        Gold = save.Gold;
        _baseAttack = save.BaseAttack;
        _baseMaxHp = save.BaseMaxHp;
        StrengthTurns = save.StrengthTurns;
        _strengthBonus = save.StrengthBonus;
        InvisibleTurns = save.InvisibleTurns;
        _turnCounter = save.TurnCounter;
        Array.Clear(SkillCooldowns);
        for (int i = 0; i < Math.Min(save.Cooldowns?.Length ?? 0, SkillCooldowns.Length); i++)
            SkillCooldowns[i] = save.Cooldowns[i];
        _perks.Clear();
        _perks.AddRange((save.Perks ?? new List<string>()).Select(ClassCatalog.FindPerk).Where(perk => perk != null));

        _pacts.Clear();
        _pacts.AddRange((save.Pacts ?? new List<string>()).Select(Roguelike.Pacts.Find).Where(pact => pact != null));
        DamageDealt = save.DamageDealt;
        KillsMade = save.KillsMade;
        BiggestHit = save.BiggestHit;
        GoldSpent = save.GoldSpent;
        PotionsDrunk = save.PotionsDrunk;
        LavaSteps = save.LavaSteps;

        Inventory.Clear();
        foreach (var item in save.Equipped.Select(s => s.ToItem()).Where(item => item != null))
            Inventory.SetEquipped(item);
        foreach (var item in save.Items.Select(s => s.ToItem()).Where(item => item != null))
            Inventory.Items.Add(item);

        RecalculateStats();
        Hp = Mathf.Clamp(save.Hp, allowDead ? 0 : 1, MaxHp);
        BodyMaterial.AlbedoColor = DisplayColor;
        Teleport(new Vector2I(save.X, save.Y));
    }

    /// <summary>Вещь на герое изменилась (заточка) — пересчитать характеристики.</summary>
    public void RefreshStats() => RecalculateStats();

    private void RecalculateStats()
    {
        var total = new Stats();
        foreach (var item in Inventory.Equipped)
            total += item.Stats;
        total += Inventory.SetBonuses + _build;
        foreach (var pact in _pacts)
            total += pact.Stats;
        foreach (var perk in _perks)
            total += perk.Bonus;
        Bonuses = total;

        Attack = _baseAttack + Bonuses.Attack + (StrengthTurns > 0 ? _strengthBonus : 0);
        Defense = Bonuses.Defense;
        MaxHp = Math.Max(_baseMaxHp + Bonuses.MaxHp, 5); // сделки могут отнять много, но герой не должен умереть от них
        Hp = Mathf.Min(Hp, MaxHp); // сняли амулет жизни — здоровье не может быть выше максимума
    }

    /// <summary>Остановить автоходьбу: дальше идти можно только отпустив клавишу и нажав снова.</summary>
    public void StopWalking() => _walkStopped = true;

    /// <summary>
    /// Автоходьба по таймеру. Встроенный автоповтор клавиатуры не используем: он слишком быстрый
    /// и зависит от настроек Windows. Вместо этого каждый кадр проверяем, держится ли клавиша.
    /// </summary>
    public override void _Process(double delta)
    {
        base._Process(delta); // «дыхание» и шаги из Actor
        UpdateFade();

        if (_heldKey == Key.None || !IsLocal)
            return;
        if (!Input.IsPhysicalKeyPressed(_heldKey))
        {
            _heldKey = Key.None; // отпустили — следующее нажатие начнёт всё заново
            return;
        }
        if (_walkStopped)
            return;

        _holdTimer -= delta;
        if (_holdTimer > 0)
            return;
        _holdTimer = WalkInterval;
        WalkRequested?.Invoke(_heldDirection);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Клавиатуру слушает только свой герой; герои друзей управляются по сети, боты — автопилотом
        if (!IsLocal || IsBot || @event is not InputEventKey { Pressed: true } key)
            return;

        // PhysicalKeycode — физическое положение клавиши, поэтому WASD работает
        // и при русской раскладке (там на этих клавишах «ЦФЫВ»)
        var direction = DirectionFor(key.PhysicalKeycode);
        if (!direction.HasValue)
            return;
        GetViewport().SetInputAsHandled();

        // Echo — это автоповтор зажатой клавиши от системы. Его просто «съедаем»: повтором управляет _Process
        if (key.Echo)
            return;

        // Сначала запоминаем удержание, потом сообщаем о нажатии: обработчик может сразу
        // вызвать StopWalking (например, рядом враг), и этот запрет не должен потеряться
        _heldKey = direction.Value != Vector2I.Zero ? key.PhysicalKeycode : Key.None; // пробел не повторяем
        _heldDirection = direction.Value;
        _holdTimer = HoldDelay;
        _walkStopped = false;
        ActionRequested?.Invoke(direction.Value);
    }

    private static Vector2I? DirectionFor(Key key) => key switch
    {
        // Стрелки и цифровой блок закреплены, остальное настраивается
        Key.Up or Key.Kp8 => new Vector2I(0, -1),
        Key.Down or Key.Kp2 => new Vector2I(0, 1),
        Key.Left or Key.Kp4 => new Vector2I(-1, 0),
        Key.Right or Key.Kp6 => new Vector2I(1, 0),
        Key.Kp7 => new Vector2I(-1, -1),
        Key.Kp9 => new Vector2I(1, -1),
        Key.Kp1 => new Vector2I(-1, 1),
        Key.Kp3 => new Vector2I(1, 1),
        Key.Kp5 => Vector2I.Zero,
        _ => KeyBindings.Find(key) switch
        {
            GameKey.Up => new Vector2I(0, -1),
            GameKey.Down => new Vector2I(0, 1),
            GameKey.Left => new Vector2I(-1, 0),
            GameKey.Right => new Vector2I(1, 0),
            GameKey.UpLeft => new Vector2I(-1, -1),
            GameKey.UpRight => new Vector2I(1, -1),
            GameKey.DownLeft => new Vector2I(-1, 1),
            GameKey.DownRight => new Vector2I(1, 1),
            GameKey.Wait => Vector2I.Zero,
            _ => null,
        },
    };
}
