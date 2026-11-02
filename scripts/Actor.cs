using Godot;

namespace Roguelike;

/// <summary>
/// Базовый класс для всех, кто ходит по карте и дерётся: игрока и врагов.
/// Хранит позицию на сетке и характеристики, а также отвечает за анимации.
/// </summary>
public partial class Actor : Node3D
{
    private const float MoveDuration = 0.1f;

    public string DisplayName { get; protected set; } = "";
    public int MaxHp { get; protected set; }
    public int Hp { get; protected set; }
    public int Attack { get; protected set; }
    public int Defense { get; protected set; }
    public Vector2I GridPos { get; private set; }
    public bool IsDead => Hp <= 0;

    protected MeshInstance3D Body;
    protected StandardMaterial3D BodyMaterial;
    protected Color BaseColor;

    private const float BarWidth = 0.7f;

    private Vector3 _bodyRestPosition;
    private Tween _moveTween;
    private Tween _attackTween;
    private Node3D _healthBar;
    private MeshInstance3D _healthFill;

    // «Дыхание»: у каждого своя фаза, чтобы все существа не дышали в унисон
    private readonly float _breathPhase = (float)GD.RandRange(0.0, Mathf.Tau);
    private float _breathTime;

    public override void _Process(double delta)
    {
        if (Body == null || Settings.AnimationScale == 0f)
            return;
        _breathTime += (float)delta;
        float breath = Mathf.Sin(_breathTime * 2.2f + _breathPhase);
        // Чуть вытягиваемся вверх и сужаемся по бокам — объём «тела» визуально сохраняется
        Body.Scale = new Vector3(1f - 0.015f * breath, 1f + 0.035f * breath, 1f - 0.015f * breath);
    }

    /// <summary>Деталь модели (голова, оружие, глаза): простая фигура с цветом и, по желанию, свечением.</summary>
    protected static MeshInstance3D Part(PrimitiveMesh mesh, Color color, Vector3 position, float glow = 0f, Vector3? rotationDegrees = null)
    {
        var material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.55f };
        if (glow > 0f)
        {
            material.EmissionEnabled = true;
            material.Emission = color;
            material.EmissionEnergyMultiplier = glow;
        }
        mesh.Material = material;
        return new MeshInstance3D { Mesh = mesh, Position = position, RotationDegrees = rotationDegrees ?? Vector3.Zero };
    }

    /// <summary>Цвет, к которому тело возвращается после вспышки урона (заморозка, невидимость меняют его).</summary>
    protected virtual Color DisplayColor => BaseColor;

    public static Vector3 GridToWorld(Vector2I p) => new(p.X, 0f, p.Y);

    protected void CreateBody(PrimitiveMesh mesh, Color color, float height)
    {
        BaseColor = color;
        BodyMaterial = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.6f };
        mesh.Material = BodyMaterial;
        _bodyRestPosition = new Vector3(0f, height, 0f);
        Body = new MeshInstance3D { Mesh = mesh, Position = _bodyRestPosition };
        AddChild(Body);
    }

    /// <summary>
    /// Полоска здоровья над головой. Камера никогда не поворачивается, поэтому достаточно
    /// один раз развернуть полоску «лицом» к камере (CameraRig.ViewBasis).
    /// </summary>
    protected void CreateHealthBar(float height)
    {
        _healthBar = new Node3D { Position = new Vector3(0f, height, 0f), Basis = CameraRig.ViewBasis, Visible = false };
        _healthBar.AddChild(CreateBarQuad(new Color(0.1f, 0.02f, 0.02f, 0.9f), 0));
        _healthFill = CreateBarQuad(new Color(0.95f, 0.2f, 0.2f), 1);
        _healthBar.AddChild(_healthFill);
        AddChild(_healthBar);
    }

    private static MeshInstance3D CreateBarQuad(Color color, int renderPriority) => new()
    {
        Mesh = new QuadMesh
        {
            Size = new Vector2(BarWidth, 0.08f),
            Material = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, // не зависит от освещения
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = true,               // видна даже за стеной
                RenderPriority = renderPriority, // заливка рисуется поверх фона
            },
        },
    };

    /// <summary>Установить здоровье напрямую (при загрузке сохранения).</summary>
    protected void SetHp(int hp)
    {
        Hp = Mathf.Clamp(hp, 0, MaxHp);
        UpdateHealthBar();
    }

    private void UpdateHealthBar()
    {
        if (_healthBar == null)
            return;
        float ratio = (float)Hp / MaxHp;
        _healthBar.Visible = Hp > 0 && Hp < MaxHp;
        // Сжимаем заливку по ширине и сдвигаем влево, чтобы она «убывала» справа
        _healthFill.Scale = new Vector3(Mathf.Max(ratio, 0.001f), 1f, 1f);
        _healthFill.Position = new Vector3(-(1f - ratio) * BarWidth / 2f, 0f, 0f);
    }

    /// <summary>Полупрозрачное тело (для призраков).</summary>
    protected void MakeTranslucent(float alpha)
    {
        BaseColor = new Color(BaseColor, alpha);
        BodyMaterial.AlbedoColor = BaseColor;
        BodyMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
    }

    /// <summary>Мгновенное перемещение (новый этаж, появление на карте).</summary>
    public void Teleport(Vector2I p)
    {
        _moveTween?.Kill();
        GridPos = p;
        Position = GridToWorld(p);
    }

    /// <summary>Логически переходим в клетку сразу, а визуально — плавно за MoveDuration.</summary>
    public void MoveTo(Vector2I p)
    {
        GridPos = p;
        _moveTween?.Kill();
        if (Settings.AnimationScale == 0f)
        {
            Position = GridToWorld(p); // анимации выключены — переходим сразу
            return;
        }
        _moveTween = CreateTween();
        _moveTween.TweenProperty(this, "position", GridToWorld(p), MoveDuration * Settings.AnimationScale);
    }

    /// <summary>Анимация удара: тело дёргается в сторону цели и возвращается.</summary>
    public void PlayAttack(Vector2I target)
    {
        var offset = new Vector3(target.X - GridPos.X, 0f, target.Y - GridPos.Y) * 0.35f;
        _attackTween?.Kill();
        Body.Position = _bodyRestPosition;
        float scale = Settings.AnimationScale;
        if (scale == 0f)
            return;
        _attackTween = CreateTween();
        _attackTween.TweenProperty(Body, "position", _bodyRestPosition + offset, 0.05f * scale);
        _attackTween.TweenProperty(Body, "position", _bodyRestPosition, 0.1f * scale);
    }

    public void TakeDamage(int amount)
    {
        Hp = Mathf.Max(Hp - amount, 0);
        UpdateHealthBar();
        // Вспышка белым и плавный возврат к обычному цвету
        BodyMaterial.AlbedoColor = Colors.White;
        CreateTween().TweenProperty(BodyMaterial, "albedo_color", DisplayColor, 0.25f);
    }

    /// <summary>Сжимаемся в точку и удаляем узел из сцены.</summary>
    public void Die()
    {
        if (Settings.AnimationScale == 0f)
        {
            QueueFree();
            return;
        }
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector3(0.01f, 0.01f, 0.01f), 0.2f * Settings.AnimationScale);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
