using Godot;

namespace Roguelike;

/// <summary>
/// Короткие визуальные эффекты, которые сами удаляются после проигрывания.
/// </summary>
public static class Effects
{
    /// <summary>false — эффекты не создаются вовсе (быстрая симуляция без графики).</summary>
    public static bool Enabled { get; set; } = true;

    private static readonly Vector3 ChestHeight = new(0f, 0.5f, 0f);
    private static readonly Vector3 HeadHeight = new(0f, 1.2f, 0f);

    /// <summary>
    /// Всплывающий текст над клеткой (урон, лечение): поднимается вверх и тает.
    /// Label3D с Billboard всегда повёрнут к камере, NoDepthTest — не прячется за стенами.
    /// </summary>
    public static void FloatingText(Node3D parent, Vector2I cell, string text, Color color)
    {
        if (!Enabled || !Settings.Current.DamageNumbers)
            return;
        var label = new Label3D
        {
            Text = text,
            FontSize = 40,
            PixelSize = 0.01f,
            Modulate = color,
            OutlineSize = 10,
            OutlineModulate = Colors.Black,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        };
        parent.AddChild(label);
        var start = Actor.GridToWorld(cell) + HeadHeight + new Vector3((float)GD.RandRange(-0.2, 0.2), 0f, 0f);
        label.Position = start;

        // Цифра «выскакивает»: появляется крупнее и быстро возвращается к своему размеру
        label.Scale = Vector3.One * 1.5f;
        var tween = label.CreateTween();
        tween.SetParallel(true); // следующие анимации идут одновременно
        tween.TweenProperty(label, "scale", Vector3.One, 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "position", start + new Vector3(0f, 0.8f, 0f), 0.7f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(label, "modulate:a", 0f, 0.7f).SetDelay(0.2f);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree)); // Chain — «после всех предыдущих»
    }

    /// <summary>Короткая вспышка света в клетке: освещает стены вокруг удара или взрыва.</summary>
    public static void Flash(Node3D parent, Vector2I cell, Color color, float energy = 3f)
    {
        if (!Enabled || !Settings.HighQuality)
            return;
        var light = new OmniLight3D { LightColor = color, LightEnergy = energy, OmniRange = 3.5f, Position = Actor.GridToWorld(cell) + ChestHeight };
        parent.AddChild(light);
        var tween = light.CreateTween();
        tween.TweenProperty(light, "light_energy", 0f, 0.22f);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }

    /// <summary>Дуга удара: тонкое кольцо раскрывается в клетке цели и тут же гаснет.</summary>
    public static void Slash(Node3D parent, Vector2I cell, Color color)
    {
        if (!Enabled || Settings.AnimationScale == 0f)
            return;
        var material = new StandardMaterial3D
        {
            AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 3f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var arc = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.27f, OuterRadius = 0.31f, Material = material },
            Position = Actor.GridToWorld(cell) + new Vector3(0f, 0.45f, 0f),
            RotationDegrees = new Vector3((float)GD.RandRange(55.0, 80.0), (float)GD.RandRange(0.0, 360.0), 0f),
            Scale = Vector3.One * 0.5f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(arc);
        var tween = arc.CreateTween().SetParallel(true);
        tween.TweenProperty(arc, "scale", Vector3.One * 1.3f, 0.16f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(material, "albedo_color:a", 0f, 0.16f);
        tween.Chain().TweenCallback(Callable.From(arc.QueueFree));
    }

    /// <summary>След на полу после смерти врага: тёмное пятно медленно исчезает.</summary>
    public static void Stain(Node3D parent, Vector2I cell, Color color)
    {
        if (!Enabled)
            return;
        var material = new StandardMaterial3D { AlbedoColor = new Color(color.Darkened(0.6f), 0.5f), Roughness = 0.3f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        var spot = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.005f, RadialSegments = 12, Material = material },
            Position = Actor.GridToWorld(cell) + new Vector3((float)GD.RandRange(-0.1, 0.1), 0.012f, (float)GD.RandRange(-0.1, 0.1)),
            Scale = new Vector3((float)GD.RandRange(0.8, 1.4), 1f, (float)GD.RandRange(0.8, 1.4)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(spot);
        var tween = spot.CreateTween();
        tween.TweenInterval(25.0);
        tween.TweenProperty(material, "albedo_color:a", 0f, 5f);
        tween.TweenCallback(Callable.From(spot.QueueFree));
    }

    /// <summary>Разлёт кубиков-осколков из клетки (удар, смерть, магия).</summary>
    public static void Burst(Node3D parent, Vector2I cell, Color color, int amount)
    {
        if (!Enabled)
            return;
        var particles = Fx.Sparks(color, amount);
        parent.AddChild(particles);
        particles.Position = Actor.GridToWorld(cell) + ChestHeight;
        particles.Emitting = true;
        parent.GetTree().CreateTimer(1.0).Timeout += particles.QueueFree;
    }

    /// <summary>
    /// Метка союзника на полу: светящийся столб и кольцо цвета его класса.
    /// Несколько секунд пульсирует, потом тает. Видна сквозь стены (NoDepthTest).
    /// </summary>
    public static void Ping(Node3D parent, Vector2I cell, Color color)
    {
        if (!Enabled)
            return;
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(color, 0.55f),
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        var ping = new Node3D { Position = Actor.GridToWorld(cell) };
        ping.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 3f, Material = material },
            Position = new Vector3(0f, 1.5f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.42f, Material = material },
            Position = new Vector3(0f, 0.05f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        ping.AddChild(ring);
        parent.AddChild(ping);

        // Кольцо «дышит» три раза, затем вся метка гаснет и удаляется
        var pulse = ring.CreateTween().SetLoops(3);
        pulse.TweenProperty(ring, "scale", new Vector3(1.6f, 1f, 1.6f), 0.5f).SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(ring, "scale", Vector3.One, 0.5f).SetTrans(Tween.TransitionType.Sine);
        var fade = ping.CreateTween();
        fade.TweenInterval(3.0);
        fade.TweenProperty(material, "albedo_color:a", 0f, 0.8f);
        fade.TweenCallback(Callable.From(ping.QueueFree));
    }

    /// <summary>Стрела летит из одной клетки в другую.</summary>
    public static void Projectile(Node3D parent, Vector2I from, Vector2I to, Color color)
    {
        if (!Enabled || Settings.AnimationScale == 0f)
            return;
        var start = Actor.GridToWorld(from) + ChestHeight;
        var end = Actor.GridToWorld(to) + ChestHeight;

        var arrow = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.05f, 0.05f, 0.45f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 1.5f,
                },
            },
        };
        arrow.AddChild(new OmniLight3D { LightColor = color, LightEnergy = 1.2f, OmniRange = 2f }); // снаряд освещает дорогу
        parent.AddChild(arrow);
        arrow.Position = start;
        arrow.LookAt(end); // длинная сторона коробки (ось Z) смотрит на цель

        var tween = arrow.CreateTween();
        tween.TweenProperty(arrow, "position", end, 0.15f * Settings.AnimationScale);
        tween.TweenCallback(Callable.From(arrow.QueueFree));
    }
}
