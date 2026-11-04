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
        AddChild(Shapes.Piece(Shapes.Cube(0.6f, 0.3f, 0.42f), Wood, new Vector3(0f, 0.15f, 0f), shadow: true));
        // Полукруглая крышка: нижняя половина цилиндра спрятана в корпусе
        AddChild(Shapes.Piece(Shapes.Rod(0.21f, 0.6f), Wood.Darkened(0.15f), new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0f, 90f), shadow: true));
        for (int i = -1; i <= 1; i++)
            AddChild(Shapes.Piece(Shapes.Cube(0.012f, 0.3f, 0.425f), Wood.Darkened(0.45f), new Vector3(i * 0.1f + 0.05f, 0.15f, 0f)));
        AddChild(Shapes.Piece(Shapes.Cube(0.62f, 0.035f, 0.44f), Metal, new Vector3(0f, 0.295f, 0f), metal: 0.5f));
        foreach (float x in new[] { -0.2f, 0.2f })
        {
            AddChild(Shapes.Piece(Shapes.Cube(0.05f, 0.31f, 0.44f), Metal, new Vector3(x, 0.155f, 0f), metal: 0.5f));
            AddChild(Shapes.Piece(Shapes.Rod(0.218f, 0.05f), Metal, new Vector3(x, 0.3f, 0f), new Vector3(0f, 0f, 90f), metal: 0.5f));
        }
        // Замок с золотой накладкой
        AddChild(Shapes.Piece(Shapes.Cube(0.11f, 0.13f, 0.03f), Metal, new Vector3(0f, 0.27f, 0.22f), metal: 0.5f));
        AddChild(Shapes.Piece(Shapes.Ball(0.035f), new Color(1f, 0.8f, 0.3f), new Vector3(0f, 0.27f, 0.24f), glow: 0.6f, metal: 0.7f));
    }
}
