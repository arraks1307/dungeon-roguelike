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
    private const double WalkInterval = 0.14;

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

    protected override Color DisplayColor => IsInvisible ? new Color(BaseColor, InvisibleAlpha) : BaseColor;

    public override void _Ready()
    {
        DisplayName = "Ты";
        CreateBody(new CapsuleMesh { Radius = 0.25f, Height = 0.9f }, Class.Color, 0.45f);
        BodyMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; // для невидимости

        // «Факел» — тёплый точечный свет, который двигается вместе с игроком
        AddChild(new OmniLight3D
        {
            Position = new Vector3(0f, 1.6f, 0f),
            LightColor = new Color(1f, 0.75f, 0.45f),
            LightEnergy = 1.8f,
            OmniRange = 8f,
            ShadowEnabled = true,
        });

        ResetStats(ClassCatalog.Warrior);
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

    public void StartCooldown(int skillIndex) =>
        SkillCooldowns[skillIndex] = Math.Max(1, Class.Skills[skillIndex].Cooldown - CooldownReduction);

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
    /// Вызывается в конце каждого хода: отсчитывает эффекты зелий, перезарядку и регенерацию.
    /// Возвращает, сколько здоровья восстановилось и какие эффекты закончились.
    /// </summary>
    public (int Healed, bool StrengthEnded, bool InvisibilityEnded) TickEffects()
    {
        _turnCounter++;
        for (int i = 0; i < SkillCooldowns.Length; i++)
            if (SkillCooldowns[i] > 0)
                SkillCooldowns[i]--;

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

    public void RestoreSave(PlayerSave save)
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
        Hp = Mathf.Clamp(save.Hp, 1, MaxHp);
        BodyMaterial.AlbedoColor = DisplayColor;
        Teleport(new Vector2I(save.X, save.Y));
    }

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
        if (_heldKey == Key.None)
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
        if (@event is not InputEventKey { Pressed: true } key)
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
