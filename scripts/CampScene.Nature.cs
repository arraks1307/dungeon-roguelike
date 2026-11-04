using System;
using System.Collections.Generic;
using Godot;
using static Roguelike.Fx;
using static Roguelike.Shapes;

namespace Roguelike;

/// <summary>
/// Природа и огонь лагеря. Трава и лес нарисованы через MultiMesh: одна фигура и тысячи её копий за один
/// вызов видеокарты, поэтому густая поляна не тормозит. Ветер считает шейдер прямо на видеокарте,
/// а не код на C#: так качаются все травинки сразу и бесплатно для процессора.
/// </summary>
public partial class CampScene
{
    public const string GrassShader = @"
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

varying float height;

void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    height = UV.y;
    float gust = sin(TIME * 0.7 + world.x * 0.35 + world.z * 0.2) * 0.5 + 0.5;
    float sway = sin(TIME * 1.9 + world.x * 1.3 + world.z * 0.9) * (0.04 + gust * 0.1);
    VERTEX.x += sway * height * height;
    VERTEX.z += sway * 0.5 * height * height;
    NORMAL = normalize(NORMAL + vec3(0.0, 1.0, 0.0));
}

void fragment() {
    ALBEDO = COLOR.rgb * mix(0.4, 1.2, height);
    ROUGHNESS = 0.55;
    SPECULAR = 0.25;
    BACKLIGHT = COLOR.rgb * 0.8 * height; // свет проходит сквозь тонкую травинку
}
";

    private const string FoliageShader = @"
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
varying vec3 world;

void vertex() {
    world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float reach = clamp(world.y * 0.22, 0.0, 1.0);
    VERTEX.x += sin(TIME * 0.9 + world.x * 0.5 + world.z * 0.4) * 0.06 * reach;
    VERTEX.z += cos(TIME * 0.7 + world.z * 0.6) * 0.04 * reach;
}

void fragment() {
    float clumps = texture(noise_tex, world.xz * 0.5 + world.y * 0.3).r;
    float needles = texture(noise_tex, world.xy * 2.1 + world.z).g;
    ALBEDO = COLOR.rgb * (0.35 + clumps * 0.8 + needles * 0.35);
    ROUGHNESS = 0.9;
    SPECULAR = 0.1;
    BACKLIGHT = vec3(0.02, 0.05, 0.025);
}
";

    private static readonly Dictionary<(Color, float), StandardMaterial3D> Surfaces = new();
    /// <summary>
    /// Материал с рельефом. Triplanar накладывает текстуру по мировым осям сразу с трёх сторон,
    /// поэтому у простых фигур нет развёртки и швов, а рисунок дерева и камня не тянется.
    /// </summary>
    private static StandardMaterial3D Surface(Color color, float metal)
    {
        if (Surfaces.TryGetValue((color, metal), out var material))
            return material;
        material = new StandardMaterial3D
        {
            AlbedoColor = color.Lightened(0.12f),
            AlbedoTexture = Detail,
            Metallic = metal,
            Roughness = metal > 0f ? 0.45f : 0.88f,
            NormalEnabled = true,
            NormalTexture = Bump,
            NormalScale = 1.1f,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = Vector3.One * 0.9f,
        };
        Surfaces[(color, metal)] = material;
        return material;
    }

    private int GrassTufts => Settings.Current.Graphics switch
    {
        GraphicsQuality.Ultra => 26000,
        GraphicsQuality.Low => 3000,
        _ => 14000,
    };

    /// <summary>Пучок из пяти изогнутых травинок: одна модель на все копии поляны.</summary>
    public static ArrayMesh CreateTuft()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new Random(5);
        for (int blade = 0; blade < 5; blade++)
        {
            float angle = (float)rng.NextDouble() * Mathf.Tau;
            float height = 0.26f + (float)rng.NextDouble() * 0.24f;
            float lean = 0.08f + (float)rng.NextDouble() * 0.12f;
            var side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.024f;
            var offset = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.14f;
            var bend = new Vector3(-side.Z, 0f, side.X).Normalized() * lean;

            Vector3 Point(float along, float width) => offset + bend * along * along + Vector3.Up * height * along + side * width;
            void Vertex(float along, float width)
            {
                tool.SetUV(new Vector2(0.5f, along));
                tool.SetNormal(new Vector3(-side.Z, 0.4f, side.X).Normalized());
                tool.AddVertex(Point(along, width));
            }

            Vertex(0f, -1f); Vertex(0f, 1f); Vertex(0.55f, 0.7f);
            Vertex(0f, -1f); Vertex(0.55f, 0.7f); Vertex(0.55f, -0.7f);
            Vertex(0.55f, -0.7f); Vertex(0.55f, 0.7f); Vertex(1f, 0f);
        }
        return tool.Commit();
    }

    /// <summary>Дорога и круг у костра вытоптаны, под постройками травы нет.</summary>
    private static bool IsBareGround(float x, float z)
    {
        float dx = x - 10f, dz = z - 8f;
        if (dx * dx + dz * dz < 19f || (z < 9f && Mathf.Abs(x - 10f) < 1.3f))
            return true;
        foreach (var spot in Spots.Values)
            if (x > spot.X - 0.8f && x < spot.X + 1.8f && z > spot.Y - 0.8f && z < spot.Y + 1.8f)
                return true;
        return false;
    }

    private void BuildGrass()
    {
        var rng = new Random(2024);
        var places = new List<Transform3D>();
        var colors = new List<Color>();
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.12f, Seed = 3 };
        for (int i = 0; i < GrassTufts * 3 && places.Count < GrassTufts; i++)
        {
            float x = (float)rng.NextDouble() * 34f - 6.5f;
            float z = (float)rng.NextDouble() * 26f - 2f;
            float density = noise.GetNoise2D(x, z) * 0.5f + 0.5f;
            if (IsBareGround(x, z) || rng.NextDouble() > 0.35 + density)
                continue;
            float scale = 0.7f + density * 0.7f + (float)rng.NextDouble() * 0.3f;
            var basis = Basis.Identity.Rotated(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau).Scaled(Vector3.One * scale);
            places.Add(new Transform3D(basis, new Vector3(x, 0f, z)));
            var dry = new Color(0.26f, 0.28f, 0.11f);
            var lush = new Color(0.12f, 0.3f, 0.12f);
            colors.Add(lush.Lerp(dry, Mathf.Clamp(0.7f - density + (float)rng.NextDouble() * 0.25f - 0.2f, 0f, 1f)));
        }

        var tuft = CreateTuft();
        tuft.SurfaceSetMaterial(0, new ShaderMaterial { Shader = new Shader { Code = GrassShader } });
        AddMultiMesh(tuft, places, colors, shadow: false);
    }

    private void AddMultiMesh(Mesh mesh, List<Transform3D> places, List<Color> colors, bool shadow)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = places.Count };
        for (int i = 0; i < places.Count; i++)
        {
            multi.SetInstanceTransform(i, places[i]);
            multi.SetInstanceColor(i, colors[i]);
        }
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>Хвойный лес вокруг поляны: ярусы веток, стволы и дальний силуэт гор.</summary>
    private void BuildForest()
    {
        var rng = new Random(1307); // лес всегда один и тот же
        var foliage = new ShaderMaterial { Shader = new Shader { Code = FoliageShader } };
        foliage.SetShaderParameter("noise_tex", Detail);

        var layers = new List<Transform3D>[4];
        var tints = new List<Color>[4];
        for (int i = 0; i < 4; i++)
        {
            layers[i] = new List<Transform3D>();
            tints[i] = new List<Color>();
        }
        var trunks = new List<Transform3D>();
        var trunkTints = new List<Color>();

        for (float x = -10f; x <= Width + 9f; x += 1.15f)
            for (float z = -4f; z <= Height + 8f; z += 1.15f)
            {
                float px = x + (float)rng.NextDouble() * 0.9f;
                float pz = z + (float)rng.NextDouble() * 0.9f;
                bool clearing = px > 0.4f && px < Width - 0.6f && pz > 1.6f;
                if (pz > Height - 3f)
                    continue; // южнее камера: дерево здесь закрыло бы собой всю поляну
                if (clearing || (pz < 2f && px > 6.5f && px < 13.5f) || rng.NextDouble() > 0.68)
                    continue;

                float scale = 0.85f + (float)rng.NextDouble() * 1.05f;
                var turn = Basis.Identity.Rotated(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau);
                trunks.Add(new Transform3D(turn.Scaled(Vector3.One * scale), new Vector3(px, 0.5f * scale, pz)));
                trunkTints.Add(new Color(0.24f, 0.16f, 0.1f));
                var tint = new Color(0.07f, 0.17f + (float)rng.NextDouble() * 0.08f, 0.06f);
                for (int level = 0; level < 4; level++)
                {
                    float size = scale * (1f - level * 0.2f);
                    layers[level].Add(new Transform3D(turn.Rotated(Vector3.Up, level * 0.9f).Scaled(new Vector3(size, scale, size)),
                        new Vector3(px, (1.0f + level * 0.72f) * scale, pz)));
                    tints[level].Add(tint.Lightened(level * 0.03f));
                }
            }

        var trunkMesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.16f, Height = 1.1f, RadialSegments = 7, Rings = 1, Material = Surface(Bark, 0f) };
        AddMultiMesh(trunkMesh, trunks, trunkTints, shadow: false);
        for (int level = 0; level < 4; level++)
        {
            var cone = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.92f - level * 0.05f, Height = 1.25f, RadialSegments = 9, Rings = 2, CapBottom = false, Material = foliage };
            AddMultiMesh(cone, layers[level], tints[level], shadow: Settings.HighQuality && level < 2);
        }

        // Дальние горы: тёмные силуэты за скалой
        var far = new Color(0.05f, 0.07f, 0.12f);
        foreach (var (x, height, radius) in new[] { (-14f, 12f, 12f), (3f, 16f, 15f), (20f, 13f, 13f), (34f, 15f, 14f) })
            Add(this, Cone(radius, height), far, x, height / 2f, -19f, shadow: false);
    }

    /// <summary>Огонь из трёх слоёв частиц: языки пламени, дым и искры, взлетающие вверх.</summary>
    private void BuildFlames(Node3D fire)
    {
        fire.AddChild(new CpuParticles3D
        {
            Amount = 32, Lifetime = 0.7f, Position = new Vector3(0f, 0.12f, 0f), Direction = Vector3.Up, Spread = 9f,
            Gravity = new Vector3(0f, 1.6f, 0f), InitialVelocityMin = 0.3f, InitialVelocityMax = 0.9f,
            ScaleAmountMin = 0.7f, ScaleAmountMax = 1.3f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.2f,
            ColorRamp = Ramp((0f, new Color(1f, 0.75f, 0.35f, 0.4f)), (0.35f, new Color(1f, 0.45f, 0.08f, 0.32f)), (0.75f, new Color(0.8f, 0.14f, 0.02f, 0.16f)), (1f, new Color(0.4f, 0.05f, 0f, 0f))),
            Mesh = Sprite(0.46f, additive: true),
        });
        fire.AddChild(new CpuParticles3D
        {
            Amount = 12, Lifetime = 3.6f, Position = new Vector3(0f, 1f, 0f), Direction = Vector3.Up, Spread = 12f,
            Gravity = new Vector3(0.12f, 0.25f, 0f), InitialVelocityMin = 0.3f, InitialVelocityMax = 0.6f,
            ScaleAmountMin = 0.8f, ScaleAmountMax = 1.8f,
            ColorRamp = Ramp((0f, new Color(0.3f, 0.3f, 0.32f, 0f)), (0.2f, new Color(0.3f, 0.3f, 0.33f, 0.07f)), (1f, new Color(0.2f, 0.2f, 0.24f, 0f))),
            Mesh = Sprite(1.1f, additive: false),
        });
        fire.AddChild(new CpuParticles3D
        {
            Amount = 24, Lifetime = 2.2f, Position = new Vector3(0f, 0.3f, 0f), Direction = Vector3.Up, Spread = 26f,
            Gravity = new Vector3(0.1f, 0.5f, 0f), InitialVelocityMin = 0.8f, InitialVelocityMax = 2f,
            ScaleAmountMin = 0.5f, ScaleAmountMax = 1.1f,
            ColorRamp = Ramp((0f, new Color(1f, 0.8f, 0.35f)), (0.7f, new Color(1f, 0.35f, 0.08f)), (1f, new Color(0.6f, 0.1f, 0f, 0f))),
            Mesh = Sprite(0.08f, additive: true),
        });
    }

    /// <summary>Светлячки над травой: живая деталь, которая сразу делает ночь уютнее.</summary>
    private void BuildFireflies()
    {
        AddChild(new CpuParticles3D
        {
            Amount = 80, Lifetime = 6f, Preprocess = 6f, LocalCoords = false,
            Position = new Vector3(Width / 2f, 0.9f, Height / 2f + 1f),
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(14f, 0.7f, 10f),
            Direction = Vector3.Up, Spread = 180f, Gravity = Vector3.Zero, InitialVelocityMin = 0.05f, InitialVelocityMax = 0.28f,
            ColorRamp = Ramp((0f, new Color(0.7f, 1f, 0.35f, 0f)), (0.25f, new Color(0.75f, 1f, 0.4f, 1f)), (0.5f, new Color(0.7f, 1f, 0.35f, 0.15f)), (0.75f, new Color(0.75f, 1f, 0.4f, 1f)), (1f, new Color(0.7f, 1f, 0.35f, 0f))),
            Mesh = Sprite(0.16f, additive: true),
        });
    }

    /// <summary>Гирлянда над дорогой к пещере: тёплые точки ведут взгляд ко входу.</summary>
    private void BuildLanterns()
    {
        foreach (var (x, z) in new[] { (7.6f, 4.2f), (12.4f, 4.2f), (7.4f, 6.6f), (12.6f, 6.6f) })
        {
            Add(this, Rod(0.06f, 2.3f), Bark, x, 1.15f, z);
            Add(this, Cube(0.5f, 0.05f, 0.05f), Bark, x + (x < 10f ? 0.2f : -0.2f), 2.2f, z);
            float hook = x + (x < 10f ? 0.42f : -0.42f);
            Add(this, Cube(0.13f, 0.2f, 0.13f), Iron, hook, 1.98f, z, metal: 0.5f);
            Add(this, Ball(0.075f), new Color(1f, 0.7f, 0.35f), hook, 1.98f, z, glow: 6f, shadow: false);
            AddChild(new OmniLight3D { LightColor = new Color(1f, 0.68f, 0.35f), LightEnergy = 1.1f, OmniRange = 4f, Position = new Vector3(hook, 1.85f, z), LightVolumetricFogEnergy = 1.5f });
        }
    }

    /// <summary>Мелкие предметы быта: поляна выглядит обжитой, а не расставленной по сетке.</summary>
    private void BuildCampDetails()
    {
        foreach (var (x, z, size) in new[] { (1.8f, 11.4f, 0.32f), (19.2f, 11f, 0.42f), (6.2f, 14.6f, 0.3f), (13.6f, 2.6f, 0.35f), (1.6f, 5.8f, 0.28f), (19f, 2.6f, 0.34f) })
        {
            var rock = new SphereMesh { Radius = size, Height = size * 1.25f, RadialSegments = 8, Rings = 4 };
            Add(this, rock, Rock, x, size * 0.4f, z, new Vector3(12f, x * 40f, 6f));
        }
        foreach (var (x, z) in new[] { (5.6f, 9.9f), (14.4f, 10.2f), (6.8f, 5.4f) })
        {
            Add(this, Rod(0.2f, 0.34f), Bark, x, 0.17f, z);
            Add(this, Rod(0.17f, 0.02f), Plank.Lightened(0.25f), x, 0.35f, z, shadow: false);
        }
        foreach (var (x, z) in new[] { (12.9f, 5.2f), (13.3f, 5.5f) })
        {
            Add(this, Taper(0.2f, 0.19f, 0.5f), Plank, x, 0.25f, z);
            Add(this, Ring(0.205f, 0.012f), Iron, x, 0.38f, z, metal: 0.5f);
            Add(this, Ring(0.205f, 0.012f), Iron, x, 0.12f, z, metal: 0.5f);
        }
    }
}
