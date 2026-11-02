namespace Roguelike;

public enum Difficulty
{
    Easy,
    Normal,
    Hard,
}

/// <summary>
/// Уровень сложности: множители здоровья и урона врагов, бонусные зелья на старте
/// и изменение количества предметов на этаже.
/// </summary>
public record DifficultyDef(Difficulty Id, string Name, float EnemyHp, float EnemyDamage, int BonusPotions, int ItemsPerFloor);

public static class Difficulties
{
    // Множители подобраны по симуляциям сотен забегов автопилотом (GameManager.SelfTest.cs, флаг --simulate)
    public static readonly DifficultyDef Easy = new(Difficulty.Easy, "Лёгкая", 0.7f, 0.7f, BonusPotions: 2, ItemsPerFloor: 1);
    public static readonly DifficultyDef Normal = new(Difficulty.Normal, "Обычная", 0.85f, 0.85f, BonusPotions: 1, ItemsPerFloor: 0);
    public static readonly DifficultyDef Hard = new(Difficulty.Hard, "Сложная", 1.1f, 1.1f, BonusPotions: 0, ItemsPerFloor: -1);

    public static readonly DifficultyDef[] All = { Easy, Normal, Hard };

    public static DifficultyDef Get(Difficulty id) => All[(int)id];
}
