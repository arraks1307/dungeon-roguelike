using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

public enum Sound
{
    Step,
    Hit,
    Kill,
    Hurt,
    Shoot,
    Pickup,
    Heal,
    Equip,
    Fire,
    Teleport,
    Stairs,
    LevelUp,
    Summon,
    Death,
    Door,
    Chest,
    Secret,
    UiHover,
    UiClick,
}

/// <summary>
/// Звуки, синтезированные прямо в коде: никаких аудиофайлов не нужно.
/// Каждый звук — это функция «время → значение волны от -1 до 1», которую мы
/// записываем в массив 16-битных сэмплов и отдаём Godot как AudioStreamWav.
/// </summary>
public partial class SoundManager : Node
{
    private const int MixRate = 22050;   // сэмплов в секунду
    private const int PoolSize = 8;      // сколько звуков может звучать одновременно

    private readonly Dictionary<Sound, AudioStreamWav> _sounds = new();
    private readonly Dictionary<Sound, float> _volumes = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private readonly Random _noise = new();
    private int _nextPlayer;

    public override void _Ready()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _players.Add(player);
        }

        // Короткий шорох шага
        Add(Sound.Step, 0.05f, -16f, t => Noise() * Decay(t, 60f));
        // Удар: шум + низкий «бум»
        Add(Sound.Hit, 0.15f, -6f, t => (Noise() * 0.6f + Sine(110f, t) * 0.7f) * Decay(t, 25f));
        // Смерть врага: падающий тон + шум
        Add(Sound.Kill, 0.3f, -6f, t => (Sine(Chirp(600f, 80f, 0.3f, t)) * 0.6f + Noise() * 0.3f) * Decay(t, 9f));
        // Получение урона: грубый падающий «квадрат»
        Add(Sound.Hurt, 0.2f, -8f, t => Square(Chirp(400f, 120f, 0.2f, t)) * 0.5f * Decay(t, 10f));
        // Выстрел: свист сверху вниз
        Add(Sound.Shoot, 0.12f, -10f, t => (Sine(Chirp(1400f, 600f, 0.12f, t)) * 0.5f + Noise() * 0.2f) * Decay(t, 20f));
        // Подбор предмета: два коротких тона вверх
        Add(Sound.Pickup, 0.16f, -8f, t => Sine(t < 0.07f ? 660f : 990f, t) * 0.6f * Decay(t, 12f));
        // Лечение: плавный подъём с дрожанием громкости
        Add(Sound.Heal, 0.35f, -8f, t => Sine(Chirp(400f, 900f, 0.35f, t)) * 0.5f * (0.7f + 0.3f * Mathf.Sin(t * 60f)) * Decay(t, 5f));
        // Надеть снаряжение: металлический лязг
        Add(Sound.Equip, 0.2f, -10f, t => (Square(1200f * t * Mathf.Tau) * 0.3f + Noise() * 0.3f) * Decay(t, 18f));
        // Свиток огня: долгий шумовой «вжух»
        Add(Sound.Fire, 0.6f, -6f, t => Noise() * 0.7f * Decay(t, 4f) * Mathf.Min(1f, t * 30f));
        // Телепорт: быстрый подъём с вибрато
        Add(Sound.Teleport, 0.35f, -8f, t => Sine(Chirp(200f, 1500f, 0.35f, t) + 20f * Mathf.Sin(t * 50f)) * 0.5f * Decay(t, 4f));
        // Лестница и новый уровень: арпеджио (ноты по очереди)
        Add(Sound.Stairs, 0.36f, -8f, t => Arpeggio(t, 0.09f, 330f, 440f, 550f, 660f) * 0.5f);
        Add(Sound.LevelUp, 0.5f, -6f, t => Arpeggio(t, 0.1f, 523f, 659f, 784f, 1047f) * 0.5f);
        // Призыв: низкое гудение
        Add(Sound.Summon, 0.5f, -6f, t => Square(Chirp(90f, 55f, 0.5f, t)) * 0.4f * Decay(t, 4f));
        // Скрип двери: низкий дрожащий тон
        Add(Sound.Door, 0.3f, -10f, t => Square(Chirp(160f, 110f, 0.3f, t)) * 0.3f * (0.6f + 0.4f * Mathf.Sin(t * 90f)) * Decay(t, 6f));
        // Открытый сундук: звон монет — быстрые высокие ноты
        Add(Sound.Chest, 0.4f, -8f, t => Arpeggio(t, 0.06f, 1047f, 1319f, 1568f, 2093f, 1568f, 2093f) * 0.4f);
        // Найден секрет: загадочный подъём
        Add(Sound.Secret, 0.5f, -8f, t => Arpeggio(t, 0.12f, 392f, 523f, 659f, 784f) * 0.45f);
        // Интерфейс: едва слышный «тик» при выборе кнопки и мягкий «клик» при нажатии
        Add(Sound.UiHover, 0.03f, -24f, t => Sine(1600f, t) * Decay(t, 120f));
        Add(Sound.UiClick, 0.08f, -14f, t => Sine(Chirp(700f, 1000f, 0.08f, t)) * 0.6f * Decay(t, 40f));
        // Смерть игрока: долгий спуск
        Add(Sound.Death, 0.9f, -4f, t => Square(Chirp(300f, 40f, 0.9f, t)) * 0.45f * Decay(t, 2.5f));
    }

    /// <summary>true — звуки не играются (быстрая симуляция).</summary>
    public bool Muted { get; set; }

    public void Play(Sound sound)
    {
        if (Muted || !_sounds.TryGetValue(sound, out var stream))
            return;

        // Берём свободный плеер, а если все заняты — по кругу перебиваем самый старый
        var player = _players.Find(p => !p.Playing) ?? _players[_nextPlayer++ % PoolSize];
        player.Stream = stream;
        player.VolumeDb = _volumes[sound];
        player.PitchScale = (float)GD.RandRange(0.93, 1.07); // лёгкий разброс, чтобы повторы не надоедали
        player.Play();
    }

    private void Add(Sound sound, float duration, float volumeDb, Func<float, float> wave)
    {
        _sounds[sound] = Synthesize(duration, wave);
        _volumes[sound] = volumeDb;
    }

    /// <summary>Переводим функцию волны в байты 16-битного звука (little-endian: младший байт первым).</summary>
    private static AudioStreamWav Synthesize(float duration, Func<float, float> wave)
    {
        int count = (int)(duration * MixRate);
        var data = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / MixRate;
            // Короткое затухание в самом конце убирает щелчок от резкого обрыва
            float fadeOut = Mathf.Min(1f, (count - i) / (MixRate * 0.01f));
            short sample = (short)(Mathf.Clamp(wave(t) * fadeOut, -1f, 1f) * short.MaxValue * 0.8f);
            data[i * 2] = (byte)(sample & 0xFF);
            data[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
    }

    // ---------- Кирпичики для звуков ----------

    private float Noise() => (float)(_noise.NextDouble() * 2.0 - 1.0);

    private static float Decay(float t, float speed) => Mathf.Exp(-t * speed);

    private static float Sine(float frequency, float t) => Mathf.Sin(Mathf.Tau * frequency * t);

    /// <summary>Синус от готовой фазы (для тонов с меняющейся частотой).</summary>
    private static float Sine(float phase) => Mathf.Sin(phase);

    private static float Square(float phase) => Mathf.Sin(phase) >= 0f ? 1f : -1f;

    /// <summary>
    /// Фаза тона, частота которого плавно меняется от f0 до f1 за duration секунд.
    /// Фаза — это интеграл частоты: 2π·(f0·t + (f1 − f0)·t² / (2·duration)).
    /// </summary>
    private static float Chirp(float f0, float f1, float duration, float t) =>
        Mathf.Tau * (f0 * t + (f1 - f0) * t * t / (2f * duration));

    private static float Arpeggio(float t, float noteLength, params float[] notes)
    {
        int index = Math.Min((int)(t / noteLength), notes.Length - 1);
        float noteTime = t - index * noteLength;
        return Sine(notes[index], t) * Decay(noteTime, 8f);
    }
}
