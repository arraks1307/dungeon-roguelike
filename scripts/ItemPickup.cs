using Godot;

namespace Roguelike;

/// <summary>
/// Предмет, лежащий на полу: медленно вращается и покачивается, чтобы его было заметно.
/// </summary>
public partial class ItemPickup : Node3D
{
    private const float FloatHeight = 0.3f;

    public Item Item { get; private set; }

    private MeshInstance3D _mesh;
    private float _time;

    public static ItemPickup Create(Item item, Vector2I cell)
    {
        var pickup = new ItemPickup { Item = item, Position = Actor.GridToWorld(cell) };

        PrimitiveMesh mesh = item.Kind switch
        {
            ItemKind.Weapon => new BoxMesh { Size = new Vector3(0.08f, 0.06f, 0.6f) },
            ItemKind.Armor => new BoxMesh { Size = new Vector3(0.4f, 0.35f, 0.15f) },
            ItemKind.HealthPotion => new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.15f, Height = 0.3f },
            _ => new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 0.4f }, // свиток
        };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = item.Color,
            EmissionEnabled = true,
            Emission = item.Color,
            EmissionEnergyMultiplier = 0.4f,
        };

        pickup._mesh = new MeshInstance3D { Mesh = mesh, Position = new Vector3(0f, FloatHeight, 0f) };
        if (item.Kind is ItemKind.TeleportScroll or ItemKind.FireScroll)
            pickup._mesh.RotationDegrees = new Vector3(0f, 0f, 90f); // свиток лежит на боку
        pickup.AddChild(pickup._mesh);
        return pickup;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Rotation = new Vector3(0f, _time * 0.8f, 0f);
        _mesh.Position = new Vector3(0f, FloatHeight + Mathf.Sin(_time * 2f) * 0.05f, 0f);
    }
}
