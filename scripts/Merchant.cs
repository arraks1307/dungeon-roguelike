using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Торговец: стоит на месте, занимает клетку. Если шагнуть в него, откроется магазин.
/// Над головой вращается монетка, чтобы его было легко узнать издалека.
/// </summary>
public partial class Merchant : Node3D
{
    public const int StockSize = 6;

    public Vector2I GridPos { get; private set; }
    public List<Item> Stock { get; } = new();

    private MeshInstance3D _coin;
    private float _time;

    public static Merchant Create(Vector2I cell, List<Item> stock)
    {
        var merchant = new Merchant { GridPos = cell, Position = Actor.GridToWorld(cell) };
        merchant.Stock.AddRange(stock);
        return merchant;
    }

    public override void _Ready()
    {
        var robe = new Color(0.45f, 0.25f, 0.6f);
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.15f, BottomRadius = 0.32f, Height = 0.9f,
                Material = new StandardMaterial3D { AlbedoColor = robe },
            },
            Position = new Vector3(0f, 0.45f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = 0.16f, Height = 0.32f,
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.8f, 0.65f) },
            },
            Position = new Vector3(0f, 1.02f, 0f),
        });
        // Остроконечный колпак
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0f, BottomRadius = 0.2f, Height = 0.35f,
                Material = new StandardMaterial3D { AlbedoColor = robe.Darkened(0.3f) },
            },
            Position = new Vector3(0f, 1.3f, 0f),
        });

        var gold = new Color(1f, 0.8f, 0.25f);
        _coin = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.03f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = gold, EmissionEnabled = true, Emission = gold, EmissionEnergyMultiplier = 1.5f,
                },
            },
            Position = new Vector3(0f, 1.75f, 0f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
        };
        AddChild(_coin);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _coin.Rotation = new Vector3(Mathf.Pi / 2f, _time * 2f, 0f);
    }
}
