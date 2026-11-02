using Godot;

namespace Roguelike;

/// <summary>
/// Сундук: занимает клетку, открывается, если в него шагнуть.
/// IsMimic — на самом деле это чудовище, которое оживёт при попытке открыть.
/// IsSecret — сундук из секретной комнаты, внутри вещи получше.
/// </summary>
public partial class Chest : Node3D
{
    private static readonly Color Wood = new(0.5f, 0.3f, 0.13f);
    private static readonly Color Metal = new(0.85f, 0.7f, 0.3f);

    public Vector2I GridPos { get; private set; }
    public bool IsMimic { get; private set; }
    public bool IsSecret { get; private set; }

    public static Chest Create(Vector2I cell, bool mimic, bool secret) =>
        new() { GridPos = cell, IsMimic = mimic, IsSecret = secret, Position = Actor.GridToWorld(cell) };

    public ChestSave CreateSave() => new() { X = GridPos.X, Y = GridPos.Y, Mimic = IsMimic, Secret = IsSecret };

    public override void _Ready()
    {
        AddPart(new BoxMesh { Size = new Vector3(0.6f, 0.32f, 0.42f) }, Wood, new Vector3(0f, 0.16f, 0f));
        AddPart(new BoxMesh { Size = new Vector3(0.62f, 0.12f, 0.44f) }, Wood.Darkened(0.2f), new Vector3(0f, 0.38f, 0f));
        // Металлические полосы и замок
        AddPart(new BoxMesh { Size = new Vector3(0.06f, 0.46f, 0.45f) }, Metal, new Vector3(-0.2f, 0.22f, 0f));
        AddPart(new BoxMesh { Size = new Vector3(0.06f, 0.46f, 0.45f) }, Metal, new Vector3(0.2f, 0.22f, 0f));
        AddPart(new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.04f) }, Metal, new Vector3(0f, 0.3f, 0.23f));
    }

    private void AddPart(BoxMesh mesh, Color color, Vector3 position)
    {
        mesh.Material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.7f };
        AddChild(new MeshInstance3D { Mesh = mesh, Position = position });
    }
}
