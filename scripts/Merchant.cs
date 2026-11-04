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
        var skin = new Color(0.95f, 0.8f, 0.65f);
        var gold = new Color(1f, 0.8f, 0.25f);
        var leather = new Color(0.42f, 0.27f, 0.15f);
        var cloth = new Color(0.72f, 0.2f, 0.18f);

        // Коврик с товаром: сразу видно, что здесь лавка
        AddChild(Shapes.Piece(Shapes.Cube(0.92f, 0.012f, 0.86f), cloth, new Vector3(0f, 0.01f, 0.02f)));
        AddChild(Shapes.Piece(Shapes.Cube(0.74f, 0.014f, 0.68f), cloth.Lightened(0.25f), new Vector3(0f, 0.012f, 0.02f)));

        AddChild(Shapes.Piece(Shapes.Taper(0.17f, 0.33f, 0.9f), robe, new Vector3(0f, 0.45f, 0f), shadow: true));
        AddChild(Shapes.Piece(Shapes.Rod(0.22f, 0.05f), leather, new Vector3(0f, 0.55f, 0f)));
        AddChild(Shapes.Piece(Shapes.Ball(0.075f), leather.Lightened(0.15f), new Vector3(0.17f, 0.46f, 0.15f)));
        AddChild(Shapes.Piece(Shapes.Ball(0.16f), skin, new Vector3(0f, 1f, 0.02f)));
        AddChild(Shapes.Piece(Shapes.Ball(0.18f), robe.Darkened(0.25f), new Vector3(0f, 1.03f, -0.05f)));
        AddChild(Shapes.Piece(Shapes.Cone(0.1f, 0.26f), new Color(0.85f, 0.83f, 0.8f), new Vector3(0f, 0.86f, 0.14f), new Vector3(188f, 0f, 0f)));
        AddChild(Shapes.Piece(Shapes.Cone(0.2f, 0.34f), robe.Darkened(0.3f), new Vector3(0f, 1.28f, -0.03f), new Vector3(-8f, 0f, 0f), shadow: true));
        AddChild(Shapes.Piece(Shapes.Ring(0.17f, 0.02f), gold, new Vector3(0f, 1.13f, -0.01f), metal: 0.7f));
        foreach (float side in new[] { -1f, 1f })
        {
            AddChild(Shapes.Piece(Shapes.Ball(0.02f), new Color(0.1f, 0.08f, 0.08f), new Vector3(side * 0.06f, 1.02f, 0.165f)));
            AddChild(Shapes.Piece(Shapes.Pill(0.055f, 0.42f), robe, new Vector3(side * 0.24f, 0.65f, 0.07f), new Vector3(-20f, 0f, side * 12f)));
            AddChild(Shapes.Piece(Shapes.Ball(0.06f), skin, new Vector3(side * 0.28f, 0.46f, 0.15f)));
        }

        // Заплечный мешок со скаткой и лампа на посохе
        AddChild(Shapes.Piece(Shapes.Cube(0.36f, 0.5f, 0.24f), leather, new Vector3(0f, 0.72f, -0.28f), shadow: true));
        AddChild(Shapes.Piece(Shapes.Rod(0.09f, 0.42f), cloth, new Vector3(0f, 1.02f, -0.28f), new Vector3(0f, 0f, 90f)));
        AddChild(Shapes.Piece(Shapes.Ball(0.09f), leather.Lightened(0.2f), new Vector3(-0.22f, 0.56f, -0.27f)));
        AddChild(Shapes.Piece(Shapes.Rod(0.02f, 1.45f), leather.Darkened(0.2f), new Vector3(0.36f, 0.72f, 0.16f)));
        AddChild(Shapes.Piece(Shapes.Cube(0.3f, 0.02f, 0.02f), leather.Darkened(0.2f), new Vector3(0.24f, 1.42f, 0.16f)));
        AddChild(Shapes.Piece(Shapes.Ball(0.065f), new Color(1f, 0.75f, 0.35f), new Vector3(0.14f, 1.3f, 0.16f), glow: 3f));
        AddChild(Shapes.Piece(Shapes.Rod(0.078f, 0.02f), gold, new Vector3(0.14f, 1.37f, 0.16f), metal: 0.7f));
        AddChild(Shapes.Piece(Shapes.Rod(0.078f, 0.02f), gold, new Vector3(0.14f, 1.23f, 0.16f), metal: 0.7f));
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.4f), LightEnergy = 0.7f, OmniRange = 2.5f, Position = new Vector3(0.14f, 1.3f, 0.3f) });

        // Монета над головой: знак торговца, виден издалека
        _coin = Shapes.Piece(Shapes.Rod(0.13f, 0.03f), gold, new Vector3(0f, 1.75f, 0f), new Vector3(90f, 0f, 0f), glow: 1.5f, metal: 0.7f);
        AddChild(_coin);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _coin.Rotation = new Vector3(Mathf.Pi / 2f, _time * 2f, 0f);
    }
}
