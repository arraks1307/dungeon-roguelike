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
    private MeshInstance3D _flame;
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
        // Тёмная чаша-держатель и второй, более яркий язычок пламени внутри основного
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.05f, Height = 0.1f, Material = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.09f, 0.08f), Metallic = 0.7f, Roughness = 0.5f } },
            Position = new Vector3(0f, -0.07f, 0f),
        });
        if (Settings.HighQuality)
            AddChild(Fx.SmallFlame(FlameColor));
        _flame = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.04f, Height = 0.15f, RadialSegments = 8, Rings = 4, Material = new StandardMaterial3D { AlbedoColor = Colors.White, EmissionEnabled = true, Emission = FlameColor.Lightened(0.5f), EmissionEnergyMultiplier = 4f } },
            Position = new Vector3(0f, 0.03f, 0f),
        };
        AddChild(_flame);

        _light = new OmniLight3D
        {
            LightColor = FlameColor,
            LightEnergy = 1.1f,
            OmniRange = 6.5f,
            OmniAttenuation = 1.4f,
            LightVolumetricFogEnergy = 2f, // в тумане вокруг факела виден ореол
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
        float flicker = 0.15f * Mathf.Sin(_time * 11f + _phase) + 0.1f * Mathf.Sin(_time * 23f + _phase * 2f);
        _light.LightEnergy = 1.5f + flicker;
        _flame.Scale = new Vector3(1f, 1f + flicker * 0.8f, 1f); // язычок дрожит вместе со светом
    }
}
