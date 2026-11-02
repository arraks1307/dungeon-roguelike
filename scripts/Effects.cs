using Godot;

namespace Roguelike;

/// <summary>
/// Короткие визуальные эффекты, которые сами удаляются после проигрывания.
/// </summary>
public static class Effects
{
    private static readonly Vector3 ChestHeight = new(0f, 0.5f, 0f);
    private static readonly Vector3 HeadHeight = new(0f, 1.2f, 0f);

    /// <summary>
    /// Всплывающий текст над клеткой (урон, лечение): поднимается вверх и тает.
    /// Label3D с Billboard всегда повёрнут к камере, NoDepthTest — не прячется за стенами.
    /// </summary>
    public static void FloatingText(Node3D parent, Vector2I cell, string text, Color color)
    {
        if (!Settings.Current.DamageNumbers)
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

        var tween = label.CreateTween();
        tween.SetParallel(true); // следующие анимации идут одновременно
        tween.TweenProperty(label, "position", start + new Vector3(0f, 0.8f, 0f), 0.7f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(label, "modulate:a", 0f, 0.7f).SetDelay(0.2f);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree)); // Chain — «после всех предыдущих»
    }

    /// <summary>Разлёт кубиков-осколков из клетки (удар, смерть, магия).</summary>
    public static void Burst(Node3D parent, Vector2I cell, Color color, int amount)
    {
        var particles = new CpuParticles3D
        {
            OneShot = true,
            Amount = amount,
            Lifetime = 0.6f,
            Explosiveness = 1f, // все частицы вылетают разом
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3.5f,
            Gravity = new Vector3(0f, -9f, 0f),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.2f,
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.07f, 0.07f, 0.07f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.5f,
                },
            },
        };
        parent.AddChild(particles);
        particles.Position = Actor.GridToWorld(cell) + ChestHeight;
        particles.Emitting = true;
        parent.GetTree().CreateTimer(1.0).Timeout += particles.QueueFree;
    }

    /// <summary>Стрела летит из одной клетки в другую.</summary>
    public static void Projectile(Node3D parent, Vector2I from, Vector2I to, Color color)
    {
        if (Settings.AnimationScale == 0f)
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
        parent.AddChild(arrow);
        arrow.Position = start;
        arrow.LookAt(end); // длинная сторона коробки (ось Z) смотрит на цель

        var tween = arrow.CreateTween();
        tween.TweenProperty(arrow, "position", end, 0.15f * Settings.AnimationScale);
        tween.TweenCallback(Callable.From(arrow.QueueFree));
    }
}
