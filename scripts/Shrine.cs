using Godot;

namespace Roguelike;

public enum ShrineKind
{
    Fountain,   // бесплатно лечит половину здоровья
    BloodAltar, // забирает треть здоровья, навсегда даёт +1 к атаке
    GoldAltar,  // за золото даёт хорошую экипировку
    Wanderer,   // странник предлагает выбор
    Pact,       // тёмный алтарь: сделка с подземельем на весь забег
    Reliquary,  // реликварий: одна вещь из трёх на выбор
    Captive,    // пленник: после спасения живёт в лагере
    Note,       // запись хрониста
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

    /// <summary>Номера сделок тёмного алтаря.</summary>
    public int[] Deals { get; set; } = System.Array.Empty<int>();

    /// <summary>Вещи реликвария.</summary>
    public System.Collections.Generic.List<Item> Offer { get; } = new();

    /// <summary>Номер пленника или записи.</summary>
    public int Extra { get; set; }

    public Color Glow => Kind switch
    {
        ShrineKind.Fountain => new Color(0.35f, 0.7f, 1f),
        ShrineKind.BloodAltar => new Color(0.9f, 0.1f, 0.15f),
        ShrineKind.Wanderer => new Color(0.5f, 1f, 0.6f),
        ShrineKind.Pact => new Color(0.65f, 0.15f, 0.9f),
        ShrineKind.Reliquary => new Color(1f, 0.6f, 0.15f),
        ShrineKind.Captive => new Color(0.95f, 0.9f, 0.7f),
        ShrineKind.Note => new Color(0.8f, 0.72f, 0.95f),
        _ => new Color(1f, 0.8f, 0.25f),
    };

    public static Shrine Create(Vector2I cell, ShrineKind kind) =>
        new() { GridPos = cell, Kind = kind, Position = Actor.GridToWorld(cell) };

    public ShrineSave CreateSave() => new()
    {
        X = GridPos.X, Y = GridPos.Y, Kind = Kind, Explained = Explained, Deals = Deals, Extra = Extra,
        Offer = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(Offer, ItemSave.From)),
    };

    public override void _Ready()
    {
        var stone = new Color(0.42f, 0.42f, 0.48f);
        // Ступени, колонна и чаша
        AddChild(Shapes.Piece(Shapes.Rod(0.44f, 0.08f), stone.Darkened(0.2f), new Vector3(0f, 0.04f, 0f), shadow: true));
        AddChild(Shapes.Piece(Shapes.Rod(0.36f, 0.08f), stone.Darkened(0.1f), new Vector3(0f, 0.12f, 0f), shadow: true));
        AddChild(Shapes.Piece(Shapes.Taper(0.16f, 0.22f, 0.36f), stone, new Vector3(0f, 0.34f, 0f), shadow: true));
        AddChild(Shapes.Piece(Shapes.Taper(0.3f, 0.16f, 0.12f), stone.Lightened(0.1f), new Vector3(0f, 0.57f, 0f), shadow: true));
        AddChild(Shapes.Piece(Shapes.Ring(0.19f, 0.015f), Glow, new Vector3(0f, 0.3f, 0f), glow: 2f));
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Tau / 4f + Mathf.Pi / 4f;
            AddChild(Shapes.Piece(Shapes.Cube(0.05f, 0.34f, 0.05f), stone.Darkened(0.05f), new Vector3(Mathf.Sin(angle) * 0.3f, 0.29f, Mathf.Cos(angle) * 0.3f)));
            AddChild(Shapes.Piece(Shapes.Ball(0.022f), Glow, new Vector3(Mathf.Sin(angle) * 0.3f, 0.48f, Mathf.Cos(angle) * 0.3f), glow: 3f));
        }

        // Кристалл парит и вращается, вокруг него наклонное кольцо
        _crystal = new Node3D { Position = new Vector3(0f, 0.92f, 0f) };
        AddChild(_crystal);
        _crystal.AddChild(Shapes.Piece(Shapes.Gem(0.17f), Glow, Vector3.Zero, glow: 2f));
        _crystal.AddChild(Shapes.Piece(Shapes.Ring(0.27f, 0.012f), Glow.Lightened(0.3f), Vector3.Zero, new Vector3(24f, 0f, 0f), glow: 2.5f));
        AddChild(new OmniLight3D { LightColor = Glow, LightEnergy = 0.8f, OmniRange = 3f, Position = new Vector3(0f, 1f, 0f) });
    }

    private Node3D _crystal;
    private float _time;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _crystal.Rotation = new Vector3(0f, _time * 0.8f, 0f);
        _crystal.Position = new Vector3(0f, 0.92f + Mathf.Sin(_time * 1.6f) * 0.04f, 0f);
    }
}
