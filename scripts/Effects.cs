using Godot;

namespace Roguelike;

/// <summary>
/// Короткие визуальные эффекты, которые сами удаляются после проигрывания.
/// </summary>
public static class Effects
{
    private static readonly Vector3 ChestHeight = new(0f, 0.5f, 0f);

    /// <summary>Стрела летит из одной клетки в другую.</summary>
    public static void Projectile(Node3D parent, Vector2I from, Vector2I to, Color color)
    {
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
        tween.TweenProperty(arrow, "position", end, 0.15f);
        tween.TweenCallback(Callable.From(arrow.QueueFree));
    }
}
