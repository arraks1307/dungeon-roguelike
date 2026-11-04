using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Игрок: читает клавиши движения и сообщает GameManager, что хочет сделать.
/// Хранит класс, характеристики, инвентарь, золото, улучшения, перезарядку способностей
/// и временные эффекты зелий. Остальные клавиши обрабатывает GameManager.
/// </summary>
public partial class Player : Actor
{
    private const int RegenInterval = 5; // регенерация срабатывает раз в столько ходов
    private const float InvisibleAlpha = 0.3f;

    // Умная ходьба: сколько держать клавишу до начала автоходьбы и пауза между её шагами (секунды)
    private const double HoldDelay = 0.3;
    private const double WalkInterval = 0.15;

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

    /// <summary>Игрок потерял связь: герой стоит на месте и ждёт, враги его не трогают.</summary>
    public bool IsOffline { get; set; }
    public int GhostReadyRound { get; set; }
    public int DamageDealt { get; set; }
    public int KillsMade { get; set; }
    public int RevivesMade { get; set; }

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
        CreateBody(new CapsuleMesh { Radius = 0.25f, Height = 0.9f }, Class.Color, 0.45f);
        BodyMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; // для невидимости

        // «Факел» — тёплый точечный свет, который двигается вместе с игроком
        // Свет повыше над головой — иначе голова пересвечена. Сам герой тень от него не отбрасывает
        // (CastShadow = Off ниже), иначе под ним лежал бы тёмный круг; враги и стены — отбрасывают
        _torchLight = new OmniLight3D
        {
            Position = new Vector3(0f, 2.4f, 0f),
            LightColor = new Color(1f, 0.75f, 0.45f),
            LightEnergy = 2.1f,
            OmniRange = 9f,
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

    /// <summary>На низком качестве — без теней от факела и без пылинок.</summary>
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
    /// Голова и снаряжение по классу: воин — шлем, меч и щит; следопыт — капюшон и лук;
    /// маг — остроконечная шляпа и посох со светящейся сферой. Детали крепятся к телу,
    /// поэтому двигаются вместе с ним при атаке и «дыхании».
    /// </summary>
    private void BuildGear()
    {
        _gear?.QueueFree();
        _gear = new Node3D();
        Body.AddChild(_gear);

        var skin = new Color(0.95f, 0.8f, 0.68f);
        var dark = Class.Color.Darkened(0.45f);
        var steel = new Color(0.78f, 0.8f, 0.86f);
        var wood = new Color(0.45f, 0.28f, 0.14f);
        _gear.AddChild(Part(new SphereMesh { Radius = 0.15f, Height = 0.3f }, skin, new Vector3(0f, 0.55f, 0.02f)));

        switch (Class.Class)
        {
            case PlayerClass.Warrior:
                _gear.AddChild(Part(new SphereMesh { Radius = 0.165f, Height = 0.2f }, steel, new Vector3(0f, 0.63f, 0f)));
                _gear.AddChild(Part(new BoxMesh { Size = new Vector3(0.05f, 0.62f, 0.03f) }, steel, new Vector3(0.31f, 0.08f, 0.12f), rotationDegrees: new Vector3(0f, 0f, -18f)));
                _gear.AddChild(Part(new BoxMesh { Size = new Vector3(0.2f, 0.04f, 0.05f) }, wood, new Vector3(0.27f, -0.15f, 0.12f)));
                _gear.AddChild(Part(new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.05f }, dark, new Vector3(-0.29f, 0.02f, 0.06f), rotationDegrees: new Vector3(0f, 0f, 90f)));
                break;

            case PlayerClass.Ranger:
                _gear.AddChild(Part(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.19f, Height = 0.28f }, dark, new Vector3(0f, 0.66f, -0.02f)));
                _gear.AddChild(Part(new TorusMesh { InnerRadius = 0.3f, OuterRadius = 0.34f }, wood, new Vector3(0.3f, 0.05f, 0.06f), rotationDegrees: new Vector3(0f, 0f, 90f)));
                _gear.AddChild(Part(new BoxMesh { Size = new Vector3(0.12f, 0.36f, 0.12f) }, wood.Darkened(0.2f), new Vector3(-0.12f, 0.15f, -0.24f), rotationDegrees: new Vector3(15f, 0f, 0f)));
                break;

            case PlayerClass.Priest:
                _gear.AddChild(Part(new TorusMesh { InnerRadius = 0.17f, OuterRadius = 0.2f }, Class.Color.Lightened(0.3f), new Vector3(0f, 0.8f, 0f), glow: 2.5f));
                _gear.AddChild(Part(new BoxMesh { Size = new Vector3(0.05f, 0.9f, 0.05f) }, wood, new Vector3(0.32f, 0.08f, 0.04f)));
                _gear.AddChild(Part(new BoxMesh { Size = new Vector3(0.26f, 0.05f, 0.05f) }, Class.Color, new Vector3(0.32f, 0.38f, 0.04f), glow: 1.5f));
                break;

            case PlayerClass.Mage:
                _gear.AddChild(Part(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.18f, Height = 0.03f }, dark, new Vector3(0f, 0.66f, 0f)));
                _gear.AddChild(Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.13f, Height = 0.4f }, dark, new Vector3(0f, 0.86f, 0f)));
                _gear.AddChild(Part(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 1.05f }, wood, new Vector3(0.32f, 0.08f, 0.04f)));
                _gear.AddChild(Part(new SphereMesh { Radius = 0.08f, Height = 0.16f }, Class.Color.Lightened(0.3f), new Vector3(0.32f, 0.64f, 0.04f), glow: 3f));
                break;
        }

        foreach (var part in _gear.GetChildren())
            ((GeometryInstance3D)part).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
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

    public int CooldownReduction => _perks.Sum(perk => perk.CooldownReduction);

    public bool Has(PerkEffect effect) => _perks.Any(perk => perk.Effect == effect);

    public IEnumerable<Perk> Perks => _perks;

    /// <summary>Постоянная прибавка к атаке (алтарь крови). Хранится в базовой атаке, поэтому попадает в сохранение.</summary>
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
                SkillCooldowns[i]--;
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
        foreach (var perk in _perks)
            total += perk.Bonus;
        Bonuses = total;

        Attack = _baseAttack + Bonuses.Attack + (StrengthTurns > 0 ? _strengthBonus : 0);
        Defense = Bonuses.Defense;
        MaxHp = _baseMaxHp + Bonuses.MaxHp;
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
        base._Process(delta); // «дыхание» из Actor
        if (_gear != null)
            _gear.Visible = !IsInvisible && !IsGhost; // невидимка и призрак прячут снаряжение

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
        Key.W or Key.Up or Key.Kp8 => new Vector2I(0, -1),
        Key.S or Key.Down or Key.Kp2 => new Vector2I(0, 1),
        Key.A or Key.Left or Key.Kp4 => new Vector2I(-1, 0),
        Key.D or Key.Right or Key.Kp6 => new Vector2I(1, 0),
        Key.Q or Key.Kp7 => new Vector2I(-1, -1),
        Key.E or Key.Kp9 => new Vector2I(1, -1),
        Key.Z or Key.Kp1 => new Vector2I(-1, 1),
        Key.C or Key.Kp3 => new Vector2I(1, 1),
        Key.Space or Key.Kp5 => Vector2I.Zero,
        _ => null,
    };
}
