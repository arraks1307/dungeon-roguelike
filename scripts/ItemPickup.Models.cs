using Godot;
using static Roguelike.Shapes;

namespace Roguelike;

/// <summary>Модели вещей на полу: предмет можно узнать по форме, не читая подсказку.</summary>
public partial class ItemPickup
{
    private static readonly Color Metal = new(0.75f, 0.77f, 0.83f);
    private static readonly Color Brass = new(0.95f, 0.75f, 0.3f);
    private static readonly Color Leather = new(0.4f, 0.26f, 0.15f);
    private static readonly Color Glass = new(0.85f, 0.92f, 1f, 0.35f);

    private void Add(PrimitiveMesh mesh, Color color, float x, float y, float z, Vector3? turn = null, float glow = 0f, float metal = 0f) =>
        _model.AddChild(Piece(mesh, color, new Vector3(x, y, z), turn, glow, metal));

    private void BuildModel(Item item)
    {
        var color = item.Color;
        switch (item.Kind)
        {
            case ItemKind.Weapon:
                Add(Cube(0.07f, 0.02f, 0.5f), color, 0f, 0f, 0.1f, glow: 0.4f, metal: 0.5f);
                Add(Cube(0.22f, 0.04f, 0.04f), Brass, 0f, 0f, -0.15f, metal: 0.7f);
                Add(Rod(0.018f, 0.13f), Leather, 0f, 0f, -0.23f, new Vector3(90f, 0f, 0f));
                Add(Ball(0.03f), Brass, 0f, 0f, -0.3f, metal: 0.7f);
                _model.RotationDegrees = new Vector3(-60f, 0f, 0f);
                break;

            case ItemKind.Armor:
                Add(Taper(0.17f, 0.14f, 0.34f), color, 0f, 0f, 0f, glow: 0.4f, metal: 0.5f);
                foreach (float side in new[] { -1f, 1f })
                    Add(Ball(0.09f, 0.75f), Metal, side * 0.19f, 0.13f, 0f, metal: 0.6f);
                Add(Ring(0.11f, 0.016f), Brass, 0f, 0.17f, 0f, metal: 0.7f);
                Add(Rod(0.15f, 0.04f), Leather, 0f, -0.13f, 0f);
                break;

            case ItemKind.Helmet:
                Add(Ball(0.16f, 0.8f), color, 0f, 0.03f, 0f, glow: 0.4f, metal: 0.5f);
                Add(Ring(0.16f, 0.018f), Brass, 0f, -0.04f, 0f, metal: 0.7f);
                Add(Cube(0.03f, 0.13f, 0.02f), Metal, 0f, -0.1f, 0.16f, metal: 0.6f);
                Add(Cube(0.03f, 0.07f, 0.24f), Brass, 0f, 0.18f, 0f, metal: 0.7f);
                break;

            case ItemKind.Ring:
                Add(Ring(0.1f, 0.02f), Brass, 0f, 0f, 0f, new Vector3(90f, 0f, 0f), metal: 0.8f);
                Add(Gem(0.05f), color, 0f, 0.13f, 0f, glow: 1.5f);
                break;

            case ItemKind.Amulet:
                Add(Ring(0.14f, 0.008f), Brass, 0f, 0.07f, 0f, new Vector3(90f, 0f, 0f), metal: 0.8f);
                Add(Rod(0.06f, 0.025f), Brass, 0f, -0.1f, 0f, new Vector3(90f, 0f, 0f), metal: 0.8f);
                Add(Gem(0.04f), color, 0f, -0.1f, 0.02f, glow: 2f);
                break;

            case ItemKind.Bomb:
                Add(Ball(0.15f), new Color(0.13f, 0.12f, 0.14f), 0f, 0f, 0f);
                Add(Ring(0.15f, 0.012f), color, 0f, 0f, 0f, metal: 0.5f);
                Add(Rod(0.04f, 0.05f), Metal, 0f, 0.16f, 0f, metal: 0.6f);
                Add(Rod(0.01f, 0.1f), Leather, 0.02f, 0.23f, 0f, new Vector3(0f, 0f, -14f));
                Add(Ball(0.028f), new Color(1f, 0.7f, 0.2f), 0.035f, 0.29f, 0f, glow: 4f);
                break;

            case ItemKind.HealthPotion or ItemKind.StrengthPotion or ItemKind.InvisibilityPotion:
                Add(Ball(0.115f), color, 0f, -0.06f, 0f, glow: 0.7f);
                Add(Ball(0.14f), Glass, 0f, -0.05f, 0f);
                Add(Rod(0.05f, 0.1f), Glass, 0f, 0.1f, 0f);
                Add(Rod(0.06f, 0.02f), Metal, 0f, 0.14f, 0f, metal: 0.6f);
                Add(Taper(0.055f, 0.042f, 0.07f), Leather, 0f, 0.18f, 0f);
                break;

            default: // свитки
                Add(Rod(0.065f, 0.36f), new Color(0.92f, 0.86f, 0.7f), 0f, 0f, 0f, new Vector3(0f, 0f, 90f));
                foreach (float side in new[] { -1f, 1f })
                    Add(Rod(0.03f, 0.05f), Leather, side * 0.2f, 0f, 0f, new Vector3(0f, 0f, 90f));
                Add(Ring(0.07f, 0.012f), color, 0f, 0f, 0f, new Vector3(0f, 0f, 90f), glow: 1.5f);
                Add(Cube(0.12f, 0.005f, 0.18f), new Color(0.92f, 0.86f, 0.7f), 0.03f, -0.07f, 0.1f, new Vector3(20f, 0f, 0f));
                break;
        }
    }

    /// <summary>Горка монет: нижние лежат плоско, верхние вразнобой.</summary>
    private void BuildCoins()
    {
        foreach (var (x, y, z, tilt) in new[]
        {
            (0f, -0.08f, 0f, 0f), (0.09f, -0.08f, 0.06f, 0f), (-0.08f, -0.08f, 0.07f, 0f), (0.01f, -0.08f, -0.1f, 0f),
            (0.03f, -0.055f, 0.03f, 10f), (-0.04f, -0.05f, -0.03f, -12f), (0f, -0.02f, 0f, 16f),
        })
            Add(Rod(0.075f, 0.02f), GoldColor, x, y, z, new Vector3(tilt, tilt * 3f, tilt), glow: 0.5f, metal: 0.7f);
    }
}
