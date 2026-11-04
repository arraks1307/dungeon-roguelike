using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Общие заготовки графики: текстуры поверхности и мягкие частицы. Их используют и лагерь, и подземелье,
/// поэтому вся игра выглядит единообразно, а текстуры создаются один раз на всю программу.
/// </summary>
public static class Fx
{
    private static readonly Dictionary<(float, bool), QuadMesh> Sprites = new();
    private static NoiseTexture2D _detail;
    private static NoiseTexture2D _bump;
    private static GradientTexture2D _softDot;

    public static NoiseTexture2D Detail => _detail ??= new NoiseTexture2D
    {
        Width = 512, Height = 512, Seamless = true,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.03f, FractalOctaves = 5 },
    };

    public static NoiseTexture2D Bump => _bump ??= new NoiseTexture2D
    {
        Width = 512, Height = 512, Seamless = true, AsNormalMap = true, BumpStrength = 6f,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.05f, FractalOctaves = 5 },
    };

    /// <summary>Мягкое пятно для частиц: радиальный градиент от белого к прозрачному.</summary>
    public static GradientTexture2D SoftDot()
    {
        if (_softDot != null)
            return _softDot;
        var ramp = new Gradient();
        ramp.SetColor(0, Colors.White);
        ramp.SetColor(1, new Color(1f, 1f, 1f, 0f));
        return _softDot = new GradientTexture2D
        {
            Gradient = ramp, Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
        };
    }

    /// <summary>Частица-спрайт: всегда смотрит на камеру, цвет берёт из настроек частиц.</summary>
    public static QuadMesh Sprite(float size, bool additive)
    {
        if (Sprites.TryGetValue((size, additive), out var cached))
            return cached;
        var sprite = new QuadMesh
        {
            Size = Vector2.One * size,
            Material = new StandardMaterial3D
            {
                AlbedoTexture = SoftDot(),
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            },
        };
        Sprites[(size, additive)] = sprite;
        return sprite;
    }

    public static Gradient Ramp(params (float At, Color Color)[] stops)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, stops[0].Color);
        ramp.SetOffset(0, stops[0].At);
        ramp.SetColor(1, stops[^1].Color);
        ramp.SetOffset(1, stops[^1].At);
        for (int i = 1; i < stops.Length - 1; i++)
            ramp.AddPoint(stops[i].At, stops[i].Color);
        return ramp;
    }

    /// <summary>Огонёк факела: тот же приём, что у костра, в меньшем масштабе. Цвет зависит от темы этажа.</summary>
    public static CpuParticles3D SmallFlame(Color color) => new()
    {
        Amount = 12, Lifetime = 0.55f, Direction = Vector3.Up, Spread = 8f,
        Gravity = new Vector3(0f, 1f, 0f), InitialVelocityMin = 0.12f, InitialVelocityMax = 0.35f,
        ScaleAmountMin = 0.7f, ScaleAmountMax = 1.2f,
        EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.05f,
        ColorRamp = Ramp((0f, new Color(color.Lightened(0.6f), 0.55f)), (0.4f, new Color(color, 0.45f)), (1f, new Color(color.Darkened(0.5f), 0f))),
        Mesh = Sprite(0.2f, additive: true),
    };

    /// <summary>Вспышка искр: удар, смерть, магия.</summary>
    public static CpuParticles3D Sparks(Color color, int amount) => new()
    {
        OneShot = true, Amount = amount, Lifetime = 0.55f, Explosiveness = 1f, Direction = Vector3.Up, Spread = 180f,
        InitialVelocityMin = 1.2f, InitialVelocityMax = 3.4f, Gravity = new Vector3(0f, -7f, 0f),
        ScaleAmountMin = 0.5f, ScaleAmountMax = 1.3f,
        ColorRamp = Ramp((0f, new Color(color.Lightened(0.5f), 1f)), (0.5f, new Color(color, 0.8f)), (1f, new Color(color.Darkened(0.4f), 0f))),
        Mesh = Sprite(0.2f, additive: true),
    };
}
