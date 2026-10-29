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
        _moveTween = CreateTween();
        _moveTween.TweenProperty(this, "position", GridToWorld(p), MoveDuration);
    }

    /// <summary>Анимация удара: тело дёргается в сторону цели и возвращается.</summary>
    public void PlayAttack(Vector2I target)
    {
        var offset = new Vector3(target.X - GridPos.X, 0f, target.Y - GridPos.Y) * 0.35f;
        _attackTween?.Kill();
        Body.Position = _bodyRestPosition;
        _attackTween = CreateTween();
        _attackTween.TweenProperty(Body, "position", _bodyRestPosition + offset, 0.05f);
        _attackTween.TweenProperty(Body, "position", _bodyRestPosition, 0.1f);
    }

    public void TakeDamage(int amount)
    {
        Hp = Mathf.Max(Hp - amount, 0);
        UpdateHealthBar();
        // Вспышка белым и плавный возврат к обычному цвету
        BodyMaterial.AlbedoColor = Colors.White;
        CreateTween().TweenProperty(BodyMaterial, "albedo_color", BaseColor, 0.25f);
    }

    /// <summary>Сжимаемся в точку и удаляем узел из сцены.</summary>
    public void Die()
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector3(0.01f, 0.01f, 0.01f), 0.2f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
