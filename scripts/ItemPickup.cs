using Godot;

namespace Roguelike;

/// <summary>
/// То, что лежит на полу: предмет или кучка золота. Медленно вращается и покачивается,
/// а редкие и эпические вещи ещё и светятся цветом редкости.
/// </summary>
public partial class ItemPickup : Node3D
{
    private const float FloatHeight = 0.3f;
    private static readonly Color GoldColor = new(1f, 0.8f, 0.25f);

    /// <summary>Предмет или null, если это золото.</summary>
    public Item Item { get; private set; }
    public int Gold { get; private set; }

    public Color MapColor => Item?.Color ?? GoldColor;

    private MeshInstance3D _mesh;
    private float _time;

    public static ItemPickup Create(Item item, Vector2I cell)
    {
        var pickup = new ItemPickup { Item = item, Position = Actor.GridToWorld(cell) };

        PrimitiveMesh mesh = item.Kind switch
        {
            ItemKind.Weapon => new BoxMesh { Size = new Vector3(0.08f, 0.06f, 0.6f) },
            ItemKind.Armor => new BoxMesh { Size = new Vector3(0.4f, 0.35f, 0.15f) },
            ItemKind.Helmet => new SphereMesh { Radius = 0.18f, Height = 0.22f },
            ItemKind.Ring => new TorusMesh { InnerRadius = 0.08f, OuterRadius = 0.14f },
            ItemKind.Amulet => new TorusMesh { InnerRadius = 0.1f, OuterRadius = 0.16f },
            ItemKind.Bomb => new SphereMesh { Radius = 0.16f, Height = 0.32f },
            ItemKind.HealthPotion or ItemKind.StrengthPotion or ItemKind.InvisibilityPotion =>
                new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.15f, Height = 0.3f },
            _ => new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 0.4f }, // свиток
        };
        pickup.AddMesh(mesh, item.Color, 0.4f);

        if (item.Kind is ItemKind.TeleportScroll or ItemKind.FireScroll or ItemKind.FreezeScroll or ItemKind.MapScroll)
            pickup._mesh.RotationDegrees = new Vector3(0f, 0f, 90f); // свиток лежит на боку
        if (item.Kind is ItemKind.Ring or ItemKind.Amulet)
            pickup._mesh.RotationDegrees = new Vector3(90f, 0f, 0f); // кольцо стоит «на ребре»

        if (item.Rarity != Rarity.Common)
        {
            pickup.AddChild(new OmniLight3D
            {
                LightColor = item.RarityColor,
                LightEnergy = item.Rarity == Rarity.Epic ? 1.2f : 0.7f,
                OmniRange = 1.5f,
                Position = new Vector3(0f, 0.4f, 0f),
            });
        }
        return pickup;
    }

    public static ItemPickup CreateGold(int amount, Vector2I cell)
    {
        var pickup = new ItemPickup { Gold = amount, Position = Actor.GridToWorld(cell) };
        pickup.AddMesh(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.18f, Height = 0.1f }, GoldColor, 0.6f);
        return pickup;
    }

    private void AddMesh(PrimitiveMesh mesh, Color color, float glow)
    {
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = glow,
        };
        _mesh = new MeshInstance3D { Mesh = mesh, Position = new Vector3(0f, FloatHeight, 0f) };
        AddChild(_mesh);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Rotation = new Vector3(0f, _time * 0.8f, 0f);
        _mesh.Position = new Vector3(0f, FloatHeight + Mathf.Sin(_time * 2f) * 0.05f, 0f);
    }
}
