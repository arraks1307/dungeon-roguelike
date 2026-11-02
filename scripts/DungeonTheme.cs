using Godot;

namespace Roguelike;

public enum DecorKind
{
    Barrel,
    Crate,
    Mushroom,
    Rock,
    Bones,
    Tombstone,
    Candle,
}

/// <summary>
/// Внешний вид этажа: цвета, освещение, декор и способ генерации.
/// Caves = true — этаж строится как пещера (клеточный автомат), а не из комнат и коридоров.
/// </summary>
public record DungeonTheme(
    string Name, Color Floor, Color Wall, Color Ambient, float AmbientEnergy, Color Torch,
    float TorchChance, bool Caves, DecorKind[] Decor);

public static class Themes
{
    public static readonly DungeonTheme Dungeon = new(
        "Подземелье", new Color(0.42f, 0.38f, 0.35f), new Color(0.55f, 0.5f, 0.46f),
        new Color(0.35f, 0.4f, 0.6f), 0.5f, new Color(1f, 0.6f, 0.2f), 1f, Caves: false,
        new[] { DecorKind.Barrel, DecorKind.Crate, DecorKind.Bones });

    public static readonly DungeonTheme Caves = new(
        "Пещеры", new Color(0.36f, 0.3f, 0.24f), new Color(0.46f, 0.37f, 0.28f),
        new Color(0.3f, 0.38f, 0.32f), 0.45f, new Color(1f, 0.7f, 0.35f), 0.5f, Caves: true,
        new[] { DecorKind.Mushroom, DecorKind.Mushroom, DecorKind.Rock });

    public static readonly DungeonTheme Crypt = new(
        "Крипта", new Color(0.3f, 0.32f, 0.33f), new Color(0.42f, 0.44f, 0.47f),
        new Color(0.28f, 0.4f, 0.34f), 0.45f, new Color(0.45f, 1f, 0.6f), 1f, Caves: false,
        new[] { DecorKind.Tombstone, DecorKind.Bones, DecorKind.Candle });

    public static readonly DungeonTheme[] All = { Dungeon, Caves, Crypt };

    /// <summary>Каждые 5 этажей — новая тема; после крипты снова по кругу.</summary>
    public static int IndexFor(int floor) => (floor - 1) / 5 % All.Length;

    public static DungeonTheme For(int floor) => All[IndexFor(floor)];
}
