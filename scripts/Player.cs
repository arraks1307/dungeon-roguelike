using System;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Игрок: читает клавиши движения и сообщает GameManager, что хочет сделать.
/// Хранит характеристики, инвентарь, золото и временные эффекты зелий.
/// Остальные клавиши (Esc, R, M, I, 1–5, колёсико) обрабатывает GameManager.
/// </summary>
public partial class Player : Actor
{
    private const int RegenInterval = 5; // регенерация срабатывает раз в столько ходов
    private const float InvisibleAlpha = 0.3f;

    /// <summary>Направление шага. Vector2I.Zero означает «пропустить ход».</summary>
    public event Action<Vector2I> ActionRequested;

    public int Level { get; private set; }
    public int Gold { get; set; }
    public Inventory Inventory { get; } = new();

    /// <summary>Сумма бонусов всей надетой экипировки.</summary>
    public Stats Bonuses { get; private set; }

    public int StrengthTurns { get; private set; }
    public int InvisibleTurns { get; private set; }
    public bool IsInvisible => InvisibleTurns > 0;
    public bool IsHurt => Hp < MaxHp;

    // Базовые значения без экипировки: растут с уровнем
    private int _baseAttack;
    private int _baseMaxHp;
    private int _strengthBonus;
    private int _turnCounter;

    protected override Color DisplayColor => IsInvisible ? new Color(BaseColor, InvisibleAlpha) : BaseColor;

    public override void _Ready()
    {
        DisplayName = "Ты";
        CreateBody(new CapsuleMesh { Radius = 0.25f, Height = 0.9f }, new Color(0.3f, 0.55f, 1f), 0.45f);
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

        ResetStats();
    }

    public void ResetStats()
    {
        Level = 1;
        Gold = 0;
        _baseAttack = 3;
        _baseMaxHp = 30;
        StrengthTurns = 0;
        InvisibleTurns = 0;
        _turnCounter = 0;
        BodyMaterial.AlbedoColor = DisplayColor;

        // Стартовый набор: кинжал в руках и одно зелье в сумке
        Inventory.Clear();
        Inventory.TryAdd(new Item(ItemCatalog.Dagger));
        Inventory.Equip(0);
        Inventory.TryAdd(new Item(ItemCatalog.HealthPotion));

        RecalculateStats();
        Hp = MaxHp;
    }

    public void LevelUp()
    {
        Level++;
        _baseMaxHp += 5;
        _baseAttack++;
        RecalculateStats();
        Heal(5);
    }

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
        Level = Level,
        Hp = Hp,
        Gold = Gold,
        BaseAttack = _baseAttack,
        BaseMaxHp = _baseMaxHp,
        StrengthTurns = StrengthTurns,
        StrengthBonus = _strengthBonus,
        InvisibleTurns = InvisibleTurns,
        TurnCounter = _turnCounter,
        Items = Inventory.Items.Select(ItemSave.From).ToList(),
        Equipped = Inventory.Equipped.Select(ItemSave.From).ToList(),
    };

    public void RestoreSave(PlayerSave save)
    {
        Level = save.Level;
        Gold = save.Gold;
        _baseAttack = save.BaseAttack;
        _baseMaxHp = save.BaseMaxHp;
        StrengthTurns = save.StrengthTurns;
        _strengthBonus = save.StrengthBonus;
        InvisibleTurns = save.InvisibleTurns;
        _turnCounter = save.TurnCounter;

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
        Bonuses = total;

        Attack = _baseAttack + Bonuses.Attack + (StrengthTurns > 0 ? _strengthBonus : 0);
        Defense = Bonuses.Defense;
        MaxHp = _baseMaxHp + Bonuses.MaxHp;
        Hp = Mathf.Min(Hp, MaxHp); // сняли амулет жизни — здоровье не может быть выше максимума
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;

        // PhysicalKeycode — физическое положение клавиши, поэтому WASD работает
        // и при русской раскладке (там на этих клавишах «ЦФЫВ»)
        Vector2I? direction = key.PhysicalKeycode switch
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

        if (direction.HasValue)
        {
            ActionRequested?.Invoke(direction.Value);
            GetViewport().SetInputAsHandled();
        }
    }
}
