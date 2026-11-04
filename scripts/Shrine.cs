using Godot;

namespace Roguelike;

public enum ShrineKind
{
    Fountain,   // бесплатно лечит половину здоровья
    BloodAltar, // забирает треть здоровья, навсегда даёт +1 к атаке
    GoldAltar,  // за золото даёт хорошую экипировку
    Wanderer,   // странник предлагает выбор
}

/// <summary>
/// Святыня на этаже: занимает клетку и срабатывает один раз. Первый шаг в неё только показывает,
/// что она делает (ход не тратится), второй — соглашается на сделку.
/// </summary>
public partial class Shrine : Node3D
{
    public Vector2I GridPos { get; private set; }
    public ShrineKind Kind { get; private set; }

    /// <summary>Описание уже показано — следующий шаг в святыню её использует.</summary>
    public bool Explained { get; set; }

    /// <summary>Кто из отряда согласился на сделку алтаря крови: она действует на всех.</summary>
    public System.Collections.Generic.HashSet<int> Votes { get; } = new();

    public Color Glow => Kind switch
    {
        ShrineKind.Fountain => new Color(0.35f, 0.7f, 1f),
        ShrineKind.BloodAltar => new Color(0.9f, 0.1f, 0.15f),
        ShrineKind.Wanderer => new Color(0.5f, 1f, 0.6f),
        _ => new Color(1f, 0.8f, 0.25f),
    };

    public static Shrine Create(Vector2I cell, ShrineKind kind) =>
        new() { GridPos = cell, Kind = kind, Position = Actor.GridToWorld(cell) };

    public ShrineSave CreateSave() => new() { X = GridPos.X, Y = GridPos.Y, Kind = Kind, Explained = Explained };

    public override void _Ready()
    {
        var stone = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.4f, 0.45f), Roughness = 0.8f };
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.4f, Height = 0.5f, Material = stone },
            Position = new Vector3(0f, 0.25f, 0f),
        });
        var glow = new StandardMaterial3D { AlbedoColor = Glow, EmissionEnabled = true, Emission = Glow, EmissionEnergyMultiplier = 2f };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.17f, Height = 0.34f, Material = glow },
            Position = new Vector3(0f, 0.68f, 0f),
        });
        AddChild(new OmniLight3D { LightColor = Glow, LightEnergy = 0.8f, OmniRange = 3f, Position = new Vector3(0f, 0.9f, 0f) });
    }
}
