using Godot;

namespace Roguelike;

/// <summary>
/// Факел на стене: держатель, огонёк и мерцающий тёплый свет.
/// Мерцание — сумма двух синусоид разной частоты, так оно выглядит менее механическим.
/// </summary>
public partial class Torch : Node3D
{
    /// <summary>Цвет пламени зависит от темы этажа (в крипте — зелёный).</summary>
    public Color FlameColor { get; set; } = new(1f, 0.6f, 0.2f);

    private OmniLight3D _light;
    private float _time;
    private float _phase;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.06f, 0.22f, 0.06f),
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.15f, 0.08f) },
            },
            Position = new Vector3(0f, -0.14f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = 0.1f, Height = 0.24f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = FlameColor, EmissionEnabled = true, Emission = FlameColor, EmissionEnergyMultiplier = 3f,
                },
            },
        });

        _light = new OmniLight3D
        {
            LightColor = FlameColor,
            LightEnergy = 1.1f,
            OmniRange = 4.5f,
            Position = new Vector3(0f, 0f, 0.3f), // чуть от стены, чтобы освещать комнату, а не камень
        };
        AddChild(_light);

        // Искры: несколько ярких точек поднимаются от пламени и гаснут.
        // ScaleAmountCurve уменьшает искру к концу жизни — она будто прогорает.
        var shrink = new Curve();
        shrink.AddPoint(new Vector2(0f, 1f));
        shrink.AddPoint(new Vector2(1f, 0f));
        AddChild(new CpuParticles3D
        {
            Amount = 6,
            Lifetime = 1.3f,
            Position = new Vector3(0f, 0.08f, 0.05f),
            Direction = Vector3.Up,
            Spread = 20f,
            Gravity = new Vector3(0f, 0.25f, 0f), // тёплый воздух тянет вверх
            InitialVelocityMin = 0.25f,
            InitialVelocityMax = 0.6f,
            ScaleAmountCurve = shrink,
            Mesh = new SphereMesh
            {
                Radius = 0.012f, Height = 0.024f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = FlameColor, EmissionEnabled = true, Emission = FlameColor, EmissionEnergyMultiplier = 4f,
                },
            },
        });

        _phase = (float)GD.RandRange(0.0, 10.0); // у каждого факела своё мерцание
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _light.LightEnergy = 1.1f + 0.15f * Mathf.Sin(_time * 11f + _phase) + 0.1f * Mathf.Sin(_time * 23f + _phase * 2f);
    }
}
