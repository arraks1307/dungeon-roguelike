using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Набор для сборки моделей из простых фигур: короткие имена фигур и готовая деталь с материалом.
///
/// Материалы хранятся в общем словаре: тысяча одинаковых деталей использует один материал, а не тысячу.
/// У деталей снижено число полигонов: мелкий шарик на экране занимает пару десятков точек,
/// и стандартные 64 сегмента ему ни к чему. На 10 врагах из 30 деталей это заметная экономия.
/// </summary>
public static class Shapes
{
    private const int DefaultRadialSegments = 64;

    private static readonly Dictionary<(Color, float, float), StandardMaterial3D> Materials = new();

    public static SphereMesh Ball(float radius, float squash = 1f) => new() { Radius = radius, Height = 2f * radius * squash };

    public static CylinderMesh Rod(float radius, float height) => Taper(radius, radius, height);

    public static CylinderMesh Taper(float top, float bottom, float height) => new() { TopRadius = top, BottomRadius = bottom, Height = height };

    public static CylinderMesh Cone(float radius, float height) => Taper(0f, radius, height);

    public static BoxMesh Cube(float x, float y, float z) => new() { Size = new Vector3(x, y, z) };

    public static TorusMesh Ring(float radius, float thickness) => new() { InnerRadius = radius - thickness, OuterRadius = radius + thickness };

    public static CapsuleMesh Pill(float radius, float height) => new() { Radius = radius, Height = height };

    /// <summary>Огранённый камень: сфера из четырёх сторон и двух поясов становится восьмигранником.</summary>
    public static SphereMesh Gem(float radius) => new() { Radius = radius, Height = radius * 2.6f, RadialSegments = 4, Rings = 2 };

    public static StandardMaterial3D Material(Color color, float glow = 0f, float metal = 0f)
    {
        if (Materials.TryGetValue((color, glow, metal), out var material))
            return material;
        material = new StandardMaterial3D { AlbedoColor = color, Roughness = metal > 0f ? 0.4f : 0.65f, Metallic = metal };
        AddRelief(material);        if (color.A < 1f)
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        if (glow > 0f)
        {
            material.EmissionEnabled = true;
            material.Emission = color;
            material.EmissionEnergyMultiplier = glow;
        }
        Materials[(color, glow, metal)] = material;
        return material;
    }

    /// <summary>
    /// Рельеф поверхности. Triplanar в координатах самой модели: рисунок не «плывёт», когда герой идёт.
    /// Без него простые фигуры выглядят гладким пластиком; у прозрачных и светящихся деталей он не нужен.
    /// </summary>
    public static void AddRelief(StandardMaterial3D material)
    {
        if (material.AlbedoColor.A < 1f)
            return;
        material.AlbedoTexture = Fx.Detail;
        material.AlbedoColor = material.AlbedoColor.Lightened(0.15f);
        material.NormalEnabled = true;
        material.NormalTexture = Fx.Bump;
        material.NormalScale = 0.7f;
        material.Uv1Triplanar = true;
        material.Uv1Scale = Vector3.One * 2.4f;
    }

    /// <summary>Убрать лишние полигоны. Фигуры, которым число сегментов задано вручную, не трогаем.</summary>
    public static void Simplify(PrimitiveMesh mesh)
    {
        switch (mesh)
        {
            case SphereMesh { RadialSegments: DefaultRadialSegments } sphere:
                sphere.RadialSegments = 14;
                sphere.Rings = 7;
                break;
            case CylinderMesh { RadialSegments: DefaultRadialSegments } cylinder:
                cylinder.RadialSegments = 12;
                cylinder.Rings = 1;
                break;
            case CapsuleMesh { RadialSegments: DefaultRadialSegments } capsule:
                capsule.RadialSegments = 12;
                capsule.Rings = 3;
                break;
            case TorusMesh { Rings: DefaultRadialSegments } torus:
                torus.Rings = 18;
                torus.RingSegments = 8;
                break;
        }
    }

    /// <summary>Деталь модели. Мелочь не отбрасывает тень: от неё почти не видно разницы, а считать дорого.</summary>
    public static MeshInstance3D Piece(PrimitiveMesh mesh, Color color, Vector3 position, Vector3? rotationDegrees = null, float glow = 0f, float metal = 0f, bool shadow = false)
    {
        Simplify(mesh);
        mesh.Material = Material(color, glow, metal);
        return new MeshInstance3D
        {
            Mesh = mesh,
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Дуга из коротких брусков в плоскости XZ, выгнутая в сторону +Z: лук, рога, рёбра.</summary>
    public static Node3D Arc(Color color, Vector3 position, float radius, float thickness, float halfAngleDegrees, int segments = 8, Vector3? rotationDegrees = null)
    {
        var arc = new Node3D { Position = position, RotationDegrees = rotationDegrees ?? Vector3.Zero };
        float step = Mathf.DegToRad(halfAngleDegrees * 2f / segments);
        float length = radius * step * 1.15f;
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.DegToRad(-halfAngleDegrees) + step * (i + 0.5f);
            arc.AddChild(Piece(Cube(length, thickness, thickness), color,
                new Vector3(Mathf.Sin(angle) * radius, 0f, (Mathf.Cos(angle) - 1f) * radius),
                new Vector3(0f, Mathf.RadToDeg(angle), 0f)));
        }
        return arc;
    }
}
