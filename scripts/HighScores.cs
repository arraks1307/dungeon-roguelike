using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Roguelike;

/// <summary>
/// Запись в таблице рекордов. Новые поля идут в конце со значениями по умолчанию,
/// поэтому старые файлы рекордов (без них) по-прежнему читаются.
/// </summary>
public record ScoreEntry(int Floor, int Kills, int Level, string Date,
    string Class = null, string Difficulty = null, bool Victory = false);

/// <summary>
/// Таблица рекордов. Хранится в JSON-файле в папке user:// — это папка данных игры,
/// на Windows она находится в %APPDATA%\Godot\app_userdata\Roguelike.
/// </summary>
public static class HighScores
{
    private const string FilePath = "user://highscores.json";
    private const int MaxEntries = 10;

    public static List<ScoreEntry> Load()
    {
        if (!FileAccess.FileExists(FilePath))
            return new List<ScoreEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<ScoreEntry>>(FileAccess.GetFileAsString(FilePath))
                   ?? new List<ScoreEntry>();
        }
        catch (JsonException)
        {
            // Файл повреждён — начинаем таблицу заново, а не роняем игру
            return new List<ScoreEntry>();
        }
    }

    /// <summary>Добавляет результат. Возвращает место в таблице (с 1) или 0, если не попал в топ.</summary>
    public static int Add(ScoreEntry entry)
    {
        var scores = Load();
        scores.Add(entry);
        scores = scores
            .OrderByDescending(score => score.Victory)
            .ThenByDescending(score => score.Floor)
            .ThenByDescending(score => score.Kills)
            .ThenByDescending(score => score.Level)
            .Take(MaxEntries)
            .ToList();
        Save(scores);
        return scores.IndexOf(entry) + 1;
    }

    private static void Save(List<ScoreEntry> scores)
    {
        using var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(scores));
    }
}
