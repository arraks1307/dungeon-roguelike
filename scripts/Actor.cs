using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Базовый класс для всех, кто ходит по карте и дерётся: игрока и врагов.
/// Хранит позицию на сетке и характеристики, а также отвечает за анимации.
/// </summary>
public partial class Actor : Node3D
{
    private const float MoveDuration = 0.14f;
    private const float StepHop = 0.07f;      // на сколько тело приподнимается в середине шага

    /// <summary>
    /// Длительность шага при удержании клавиши. Чуть больше паузы между шагами (Player.WalkInterval):
    /// новый шаг начинается раньше, чем кончился старый, поэтому герой не замирает на каждой клетке.
    /// </summary>
    private const float ContinuousStepSeconds = 0.16f;

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
    private Tween _barTween;
    private float _shownRatio = 1f;

    // «Дыхание»: у каждого своя фаза, чтобы все существа не дышали в унисон
    private readonly float _breathPhase = (float)GD.RandRange(0.0, Mathf.Tau);
    private float _breathTime;

    // Шарниры модели: конечности качаются при ходьбе, рука с оружием замахивается при ударе
    private readonly List<(Node3D Pivot, float Swing, float Home)> _limbs = new();
    private float _stride;
    private float _walkBlend;
    private float _weaponRest;
    private Tween _weaponTween;
    private StandardMaterial3D _hitOverlay;
    private Tween _flashTween;

    protected Node3D WeaponArm;
    protected Node3D Cape;
    protected Node3D Spinner;           // деталь, которая медленно вращается (души вокруг некроманта)
    protected float HoverHeight;        // больше нуля: существо парит и покачивается

    /// <summary>Во сколько раз сильнее обычного «дышит» тело (у слизня это колыхание).</summary>
    protected virtual float BreathAmount => 1f;

    public override void _Process(double delta)
    {
        if (Body == null || Settings.AnimationScale == 0f)
            return;
        float step = (float)delta;
        _breathTime += step;
        float breath = Mathf.Sin(_breathTime * 2.2f + _breathPhase) * BreathAmount;
        // Чуть вытягиваемся вверх и сужаемся по бокам — объём «тела» визуально сохраняется
        Body.Scale = new Vector3(1f - 0.015f * breath, 1f + 0.035f * breath, 1f - 0.015f * breath);

        bool moving = _moveTween != null && _moveTween.IsValid() && _moveTween.IsRunning();
        bool attacking = _attackTween != null && _attackTween.IsValid() && _attackTween.IsRunning();
        _walkBlend = Mathf.MoveToward(_walkBlend, moving ? 1f : 0f, step * 9f);
        if (moving)
            _stride += step * 17f;
        AnimateLimbs();

        if (Cape != null)
            Cape.Rotation = new Vector3(0.1f + 0.55f * _walkBlend + 0.04f * Mathf.Sin(_breathTime * 1.7f), 0f, 0f);
        Spinner?.RotateY(step * 0.9f);
        if (HoverHeight > 0f && !attacking)
            Body.Position = _bodyRestPosition + Vector3.Up * Mathf.Sin(_breathTime * 1.8f + _breathPhase) * HoverHeight;
    }

    private void AnimateLimbs()
    {
        bool swinging = _weaponTween != null && _weaponTween.IsValid() && _weaponTween.IsRunning();
        foreach (var (pivot, swing, home) in _limbs)
        {
            if (swinging && pivot == WeaponArm)
                continue;
            pivot.Rotation = new Vector3(home + swing * Mathf.Sin(_stride) * 0.75f * _walkBlend, 0f, 0f);
        }
    }

    /// <summary>Деталь модели (голова, оружие, глаза): простая фигура с цветом и, по желанию, свечением.</summary>
    protected static MeshInstance3D Part(PrimitiveMesh mesh, Color color, Vector3 position, float glow = 0f, Vector3? rotationDegrees = null) =>
        Shapes.Piece(mesh, color, position, rotationDegrees, glow);

    /// <summary>Деталь с материалом тела: вместе с ним меняет цвет от мороза и невидимости.</summary>
    protected MeshInstance3D Flesh(PrimitiveMesh mesh, Vector3 position, Vector3? rotationDegrees = null)
    {
        Shapes.Simplify(mesh);
        mesh.Material = BodyMaterial;
        return new MeshInstance3D { Mesh = mesh, Position = position, RotationDegrees = rotationDegrees ?? Vector3.Zero };
    }

    /// <summary>Конечность: шарнир в плече или бедре, а сама фигура свисает вниз от него.</summary>
    private Node3D AddLimb(Node3D parent, Vector3 pivot, float swing, float rest, float length, float radius, Color? color)
    {
        var limb = new Node3D { Position = pivot, Rotation = new Vector3(rest, 0f, 0f) };
        var position = new Vector3(0f, -length / 2f, 0f);
        limb.AddChild(color is { } solid
            ? Shapes.Piece(Shapes.Pill(radius, length), solid, position, shadow: true)
            : Flesh(Shapes.Pill(radius, length), position));
        parent.AddChild(limb);
        _limbs.Add((limb, swing, rest));
        return limb;
    }

    /// <summary>Две ноги с ботинками. color = null — цвет тела.</summary>
    protected void AddLegs(Node3D parent, float hipY, float hipX, float length, float radius, Color? color, Color boot)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var leg = AddLimb(parent, new Vector3(side * hipX, hipY, 0f), side, 0f, length, radius, color);
            leg.AddChild(Shapes.Piece(Shapes.Cube(radius * 2.3f, 0.08f, radius * 3.4f), boot, new Vector3(0f, 0.04f - length, radius * 0.6f)));
        }
    }

    /// <summary>
    /// Две руки с кистями. Вещи в руке крепятся к шарниру на расстоянии длины руки вниз (y = -length).
    /// Рука с оружием (со стороны +X) почти не качается при ходьбе и замахивается при ударе.
    /// </summary>
    protected (Node3D Weapon, Node3D Free) AddArms(Node3D parent, float shoulderY, float shoulderX, float length, float radius, Color? color, Color hand, float weaponRest = -0.5f)
    {
        var weapon = AddLimb(parent, new Vector3(shoulderX, shoulderY, 0f), 0.15f, weaponRest, length, radius, color);
        var free = AddLimb(parent, new Vector3(-shoulderX, shoulderY, 0f), 0.7f, -0.25f, length, radius, color);
        foreach (var arm in new[] { weapon, free })
            arm.AddChild(Shapes.Piece(Shapes.Ball(radius * 1.25f), hand, new Vector3(0f, -length, 0f)));
        WeaponArm = weapon;
        _weaponRest = weaponRest;
        return (weapon, free);
    }

    /// <summary>Плащ висит от плеч и отлетает назад при ходьбе (см. _Process).</summary>
    protected Node3D AddCape(Node3D parent, Vector3 position, float width, float height, Color color)
    {
        var cape = new Node3D { Position = position };
        cape.AddChild(Shapes.Piece(Shapes.Cube(width, height, 0.025f), color, new Vector3(0f, -height / 2f, 0f), shadow: true));
        parent.AddChild(cape);
        return cape;
    }

    protected void ClearLimbs()
    {
        _limbs.Clear();
        WeaponArm = null;
        Cape = null;
    }

    /// <summary>Все фигуры модели вместе с вложенными деталями. Полоска здоровья сюда не входит.</summary>
    protected IEnumerable<MeshInstance3D> ModelMeshes()
    {
        var stack = new Stack<Node>();
        stack.Push(Body);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D mesh)
                yield return mesh;
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
    }

    /// <summary>
    /// Вспышка урона по всей модели. MaterialOverlay рисует поверх фигуры второй слой,
    /// поэтому белеют и броня, и оружие, а их собственные материалы не меняются.
    /// </summary>
    private void FlashModel()
    {
        if (Body == null || !Effects.Enabled)
            return;
        _hitOverlay ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _hitOverlay.AlbedoColor = new Color(1f, 1f, 1f, 0.8f);
        foreach (var mesh in ModelMeshes())
            mesh.MaterialOverlay = _hitOverlay;

        _flashTween?.Kill();
        _flashTween = CreateTween();
        _flashTween.TweenProperty(_hitOverlay, "albedo_color:a", 0f, 0.22f);
        _flashTween.TweenCallback(Callable.From(() =>
        {
            foreach (var mesh in ModelMeshes())
                mesh.MaterialOverlay = null;
        }));
    }

    /// <summary>Цвет, к которому тело возвращается после вспышки урона (заморозка, невидимость меняют его).</summary>
    protected virtual Color DisplayColor => BaseColor;

    public static Vector3 GridToWorld(Vector2I p) => new(p.X, 0f, p.Y);

    protected void CreateBody(PrimitiveMesh mesh, Color color, float height)
    {
        BaseColor = color;
        // Rim — светлая кайма по контуру: фигура читается на тёмном фоне
        BodyMaterial = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.5f, RimEnabled = true, Rim = 0.45f, RimTint = 0.6f };
        Shapes.AddRelief(BodyMaterial);
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
        _barTween?.Kill();
        if (Settings.AnimationScale == 0f || !Effects.Enabled)
        {
            ShowHealthRatio(ratio);
            return;
        }
        _barTween = CreateTween();
        _barTween.TweenMethod(Callable.From<float>(ShowHealthRatio), _shownRatio, ratio, 0.2f * Settings.AnimationScale)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    private void ShowHealthRatio(float ratio)
    {
        _shownRatio = ratio;
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

    private Tween _turnTween;
    private float _facing;

    /// <summary>Плавно развернуться в сторону движения или удара.</summary>
    private void Face(Vector2I direction)
    {
        if (Body == null || direction == Vector2I.Zero || Settings.AnimationScale == 0f || !Effects.Enabled)
            return;
        float from = _facing;
        float to = Mathf.Atan2(direction.X, direction.Y);
        _turnTween?.Kill();
        _turnTween = CreateTween();
        _turnTween.TweenMethod(Callable.From<float>(t =>
        {
            _facing = Mathf.LerpAngle(from, to, t);
            ApplyFacing(_facing);
        }), 0f, 1f, 0.12f * Settings.AnimationScale);
    }

    protected virtual void ApplyFacing(float angle) => Body.Rotation = new Vector3(0f, angle, 0f);

    /// <summary>Идёт непрерывная ходьба: шаги перетекают друг в друга с постоянной скоростью.</summary>
    protected virtual bool ContinuousMove => false;

    /// <summary>
    /// Логически переходим в клетку сразу, а визуально — плавно. Одиночный шаг мягко тормозит,
    /// непрерывная ходьба идёт ровно. В середине шага тело чуть приподнимается: так видно походку.
    /// </summary>
    public void MoveTo(Vector2I p)
    {
        Face(p - GridPos);
        GridPos = p;
        _moveTween?.Kill();
        if (Settings.AnimationScale == 0f || !Effects.Enabled)
        {
            Position = GridToWorld(p); // анимации выключены — переходим сразу
            return;
        }

        var from = new Vector3(Position.X, 0f, Position.Z);
        var to = GridToWorld(p);
        bool continuous = ContinuousMove;
        _moveTween = CreateTween();
        _moveTween.TweenMethod(Callable.From<float>(t =>
                Position = from.Lerp(to, t) + Vector3.Up * Mathf.Sin(t * Mathf.Pi) * StepHop),
                0f, 1f, continuous ? ContinuousStepSeconds : MoveDuration * Settings.AnimationScale)
            .SetTrans(continuous ? Tween.TransitionType.Linear : Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    /// <summary>Анимация удара: тело дёргается в сторону цели и возвращается.</summary>
    public void PlayAttack(Vector2I target)
    {
        Face(target - GridPos);
        var offset = new Vector3(target.X - GridPos.X, 0f, target.Y - GridPos.Y) * 0.35f;
        _attackTween?.Kill();
        Body.Position = _bodyRestPosition;
        float scale = Settings.AnimationScale;
        if (scale == 0f)
            return;
        _attackTween = CreateTween();
        // Быстрый выпад с разгоном и мягкий возврат
        _attackTween.TweenProperty(Body, "position", _bodyRestPosition + offset, 0.06f * scale)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _attackTween.TweenProperty(Body, "position", _bodyRestPosition, 0.14f * scale)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        if (WeaponArm == null)
            return;
        // Рука сначала заносит оружие, потом рубит вниз и возвращается
        _weaponTween?.Kill();
        _weaponTween = CreateTween();
        _weaponTween.TweenProperty(WeaponArm, "rotation:x", _weaponRest - 1.3f, 0.06f * scale)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _weaponTween.TweenProperty(WeaponArm, "rotation:x", _weaponRest + 0.8f, 0.07f * scale)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _weaponTween.TweenProperty(WeaponArm, "rotation:x", _weaponRest, 0.16f * scale)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    public void TakeDamage(int amount)
    {
        Hp = Mathf.Max(Hp - amount, 0);
        UpdateHealthBar();
        // Вспышка белым и плавный возврат к обычному цвету
        BodyMaterial.AlbedoColor = Colors.White;
        CreateTween().TweenProperty(BodyMaterial, "albedo_color", DisplayColor, 0.25f);
        FlashModel();
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
        tween.TweenProperty(this, "scale", new Vector3(0.01f, 0.01f, 0.01f), 0.25f * Settings.AnimationScale)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
