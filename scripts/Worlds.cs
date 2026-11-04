using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Roguelike;

/// <summary>Сколько этажей в мире: финальный босс всегда стоит на кратном пяти этаже.</summary>
public record WorldLength(string Name, int FinalFloor);

/// <summary>Вложение очков в героя при создании мира.</summary>
public record HeroStat(string Name, string Effect, int Cost, Stats Bonus = default, int Gold = 0, int Potions = 0);

/// <summary>Черта героя: постоянный плюс с платой за него.</summary>
public record HeroTrait(string Name, string Effect, Stats Bonus = default);

/// <summary>
/// Описание мира: всё, что игрок выбрал при создании. Герой, очки и черта здесь неизменны:
/// никакой код после создания их не меняет, только счётчик спусков растёт.
/// </summary>
public class WorldInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public int LengthIndex { get; set; } = 1;
    public int EventsIndex { get; set; } = 1;
    public bool Permadeath { get; set; }
    public int ClassIndex { get; set; }
    public int[] Points { get; set; } = new int[Worlds.Stats.Length];
    public int Trait { get; set; }
    public int Descents { get; set; }

    public ClassDef Class => ClassCatalog.All[ClassIndex];
    public int FinalFloor => Worlds.Lengths[LengthIndex].FinalFloor;
    public double EventScale => Worlds.EventScales[EventsIndex];
    public string ModeName => Permadeath ? "вечная смерть" : "возвращение в лагерь";
}

/// <summary>
/// Сохранения-миры. У каждого своя папка: описание мира, лагерь с прогрессом и текущий спуск.
/// Выбор мира переключает пути Profile и SaveSystem, поэтому остальной код работает как раньше.
/// </summary>
public static class Worlds
{
    public const int PointBudget = 8;
    public const int MaxPerStat = 5;

    public static readonly WorldLength[] Lengths = { new("Короткий, 10 этажей", 10), new("Обычный, 15 этажей", 15), new("Длинный, 20 этажей", 20) };
    public static readonly string[] EventNames = { "Редкие", "Обычные", "Частые" };
    public static readonly double[] EventScales = { 0.6, 1.0, 1.4 };

    public static readonly HeroStat[] Stats =
    {
        new("Здоровье", "+5 к здоровью", 1, new Stats(MaxHp: 5)),
        new("Атака", "+1 к атаке", 2, new Stats(Attack: 1)),
        new("Защита", "+1 к защите", 3, new Stats(Defense: 1)),
        new("Меткость", "+5% шанс крита", 1, new Stats(Crit: 5)),
        new("Восстановление", "+1 к регенерации", 3, new Stats(Regen: 1)),
        new("Кошелёк", "+30 золота на старте", 1, Gold: 30),
        new("Запас зелий", "+1 зелье на старте", 1, Potions: 1),
    };

    public static readonly HeroTrait[] Traits =
    {
        new("Без черты", "обычный герой"),
        new("Закалённый", "+10 здоровья, но −1 атаки", new Stats(Attack: -1, MaxHp: 10)),
        new("Яростный", "+2 атаки, но −8 здоровья", new Stats(Attack: 2, MaxHp: -8)),
        new("Осторожный", "+1 защиты, но −1 атаки", new Stats(Attack: -1, Defense: 1)),
        new("Зоркий", "+2 обзора, но −5 здоровья", new Stats(MaxHp: -5, Vision: 2)),
        new("Живучий", "+1 регенерации, но −1 защиты", new Stats(Defense: -1, Regen: 1)),
        new("Кровожадный", "+1 лечения за удар, но −6 здоровья", new Stats(MaxHp: -6, Lifesteal: 1)),
    };

    /// <summary>Самопроверка подменяет папку и не трогает миры игрока.</summary>
    public static string Folder { get; set; } = "user://worlds";

    public static WorldInfo Current { get; private set; }

    private static string _defaultProfile;
    private static string _defaultSave;

    private static string UnlockFile => $"{Folder}/unlocked.json";

    private static string Directory(WorldInfo world) => $"{Folder}/{world.Id}";

    public static int Spent(WorldInfo world) => world.Points.Select((points, i) => points * Stats[i].Cost).Sum();

    public static Stats BuildBonus(WorldInfo world)
    {
        var total = Traits[world.Trait].Bonus;
        for (int i = 0; i < Stats.Length; i++)
            for (int point = 0; point < world.Points[i]; point++)
                total += Stats[i].Bonus;
        return total;
    }

    public static int StartGold(WorldInfo world) => Enumerable.Range(0, Stats.Length).Sum(i => world.Points[i] * Stats[i].Gold);

    public static int StartPotions(WorldInfo world) => Enumerable.Range(0, Stats.Length).Sum(i => world.Points[i] * Stats[i].Potions);

    /// <summary>Описание героя для экранов: что выбрано при создании.</summary>
    public static string HeroSummary(WorldInfo world)
    {
        var picks = Enumerable.Range(0, Stats.Length).Where(i => world.Points[i] > 0)
            .Select(i => $"{Stats[i].Name} {world.Points[i]}").ToList();
        if (world.Trait > 0)
            picks.Add(Traits[world.Trait].Name);
        return picks.Count == 0 ? world.Class.Name : $"{world.Class.Name}: {string.Join(", ", picks)}";
    }

    /// <summary>Зерно из текста: одно и то же слово всегда даёт один и тот же мир.</summary>
    public static int ParseSeed(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (int)(Time.GetTicksUsec() & 0x7FFFFFFF);
        if (int.TryParse(text.Trim(), out int number))
            return number;
        int hash = 17;
        foreach (char symbol in text.Trim())
            hash = unchecked(hash * 31 + symbol);
        return hash;
    }

    public static List<WorldInfo> LoadAll()
    {
        var result = new List<WorldInfo>();
        using var folder = DirAccess.Open(Folder);
        if (folder == null)
            return result;
        foreach (string name in folder.GetDirectories())
        {
            string path = $"{Folder}/{name}/world.json";
            if (!FileAccess.FileExists(path))
                continue;
            try
            {
                var world = JsonSerializer.Deserialize<WorldInfo>(FileAccess.GetFileAsString(path));
                if (world != null)
                    result.Add(world);
            }
            catch (JsonException exception)
            {
                GD.PushWarning($"Мир {name} повреждён: {exception.Message}");
            }
        }
        return result.OrderBy(world => world.Name).ToList();
    }

    public static void Create(WorldInfo world)
    {
        world.Id = $"{DateTime.Now:yyyyMMddHHmmss}_{Math.Abs(world.Seed) % 100000}";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Directory(world)));
        Save(world);
    }

    public static void Save(WorldInfo world)
    {
        using var file = FileAccess.Open($"{Directory(world)}/world.json", FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(world));
    }

    /// <summary>Открыть мир: лагерь и текущий спуск теперь читаются из его папки.</summary>
    public static void Select(WorldInfo world)
    {
        if (Current == null)
        {
            _defaultProfile = Profile.FilePath;
            _defaultSave = SaveSystem.FilePath;
        }
        Current = world;
        Profile.FilePath = $"{Directory(world)}/camp.json";
        SaveSystem.FilePath = $"{Directory(world)}/descent.json";
        Profile.Load();
    }

    public static void Close()
    {
        if (Current == null)
            return;
        Current = null;
        Profile.FilePath = _defaultProfile;
        SaveSystem.FilePath = _defaultSave;
        Profile.Load();
    }

    public static void Delete(WorldInfo world)
    {
        if (Current == world)
            Close();
        string path = Directory(world);
        foreach (string file in new[] { "world.json", "camp.json", "descent.json" })
            if (FileAccess.FileExists($"{path}/{file}"))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath($"{path}/{file}"));
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    // Героя выбирают до появления лагеря, поэтому открытия классов общие для всех миров
    public static HashSet<string> LoadUnlocks()
    {
        if (!FileAccess.FileExists(UnlockFile))
            return new HashSet<string>();
        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(FileAccess.GetFileAsString(UnlockFile)) ?? new HashSet<string>();
        }
        catch (JsonException)
        {
            return new HashSet<string>();
        }
    }

    public static void AddUnlock(string achievement)
    {
        var unlocks = LoadUnlocks();
        if (!unlocks.Add(achievement))
            return;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Folder));
        using var file = FileAccess.Open(UnlockFile, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(unlocks));
    }
}
