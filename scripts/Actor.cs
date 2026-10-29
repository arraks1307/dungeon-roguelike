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

    private Vector3 _bodyRestPosition;
    private Tween _moveTween;
    private Tween _attackTween;

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
