namespace Roguelike;

public enum Difficulty
{
    Easy,
    Normal,
    Hard,
    Nightmare,
}

/// <summary>
/// Уровень сложности: множители здоровья и урона врагов, бонусные зелья на старте
/// изменение количества предметов на этаже, доля элитных врагов и прибавка к их числу.
/// </summary>
public record DifficultyDef(Difficulty Id, string Name, float EnemyHp, float EnemyDamage, int BonusPotions, int ItemsPerFloor,
    double EliteChance = 0, float ExtraEnemies = 0f);

public static class Difficulties
{
    // Множители подобраны по симуляциям сотен забегов автопилотом (GameManager.SelfTest.cs, флаг --simulate)
    public static readonly DifficultyDef Easy = new(Difficulty.Easy, "Лёгкая", 0.7f, 0.7f, BonusPotions: 2, ItemsPerFloor: 1);
    public static readonly DifficultyDef Normal = new(Difficulty.Normal, "Обычная", 0.9f, 0.9f, BonusPotions: 1, ItemsPerFloor: 0, EliteChance: 0.1);

    // Сложная и Кошмар рассчитаны на живого игрока: он намного умнее автопилота, поэтому здесь бот гибнет быстро
    public static readonly DifficultyDef Hard = new(Difficulty.Hard, "Сложная", 1.35f, 1.3f, BonusPotions: 0, ItemsPerFloor: -1, EliteChance: 0.2, ExtraEnemies: 0.25f);
    public static readonly DifficultyDef Nightmare = new(Difficulty.Nightmare, "Кошмар", 1.8f, 1.7f, BonusPotions: 0, ItemsPerFloor: -2, EliteChance: 0.35, ExtraEnemies: 0.5f);

    public static readonly DifficultyDef[] All = { Easy, Normal, Hard, Nightmare };

    public static DifficultyDef Get(Difficulty id) => All[(int)id];
}
