using System.Text.Json;
using Godot;

namespace Roguelike;

public enum AnimationSpeed
{
    Normal,
    Fast,
    Instant,
}

/// <summary>Настройки игрока. Хранятся в user://settings.json.</summary>
public class GameSettings
{
    public int Volume { get; set; } = 80;
    public bool Fullscreen { get; set; }
    public AnimationSpeed Animation { get; set; } = AnimationSpeed.Normal;
    public bool DamageNumbers { get; set; } = true;
    public bool CameraShake { get; set; } = true;
}

/// <summary>
/// Доступ к настройкам из любого места игры. static — потому что настройки одни на всю программу.
/// </summary>
public static class Settings
{
    private const string FilePath = "user://settings.json";

    public static GameSettings Current { get; private set; } = new();

    /// <summary>Множитель длительности анимаций: 1 — обычные, 0.5 — быстрые, 0 — без анимаций.</summary>
    public static float AnimationScale => Current.Animation switch
    {
        AnimationSpeed.Fast => 0.5f,
        AnimationSpeed.Instant => 0f,
        _ => 1f,
    };

    public static void Load()
    {
        if (!FileAccess.FileExists(FilePath))
            return;
        try
        {
            Current = JsonSerializer.Deserialize<GameSettings>(FileAccess.GetFileAsString(FilePath)) ?? new GameSettings();
        }
        catch (JsonException)
        {
            Current = new GameSettings(); // повреждённый файл — просто берём настройки по умолчанию
        }
    }

    public static void Save()
    {
        using var file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(Current));
    }

    /// <summary>Применяет настройки к движку: громкость главной шины звука и режим окна.</summary>
    public static void Apply()
    {
        // Громкость в Godot задаётся в децибелах, а нам удобнее проценты — переводим
        AudioServer.SetBusMute(0, Current.Volume == 0);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Current.Volume / 100f));

        var mode = Current.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
    }
}
