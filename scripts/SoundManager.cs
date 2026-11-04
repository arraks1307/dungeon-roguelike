using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    Drip,
    UiHover,
    UiClick,
}

/// <summary>
/// Звуки, синтезированные прямо в коде: никаких аудиофайлов не нужно.
/// Каждый звук — это функция «время → значение волны от -1 до 1», которую мы
/// записываем в массив 16-битных сэмплов и отдаём Godot как AudioStreamWav.
///
/// Характер — тёмное каменное подземелье. Основные приёмы:
/// - отфильтрованный шум: сырой белый шум звучит как шипение, а без верхних частот — как удар, шорох, огонь;
/// - «чирп» (тон с падающей частотой) даёт ударам вес;
/// - негармонические обертоны звучат как металл, колокол, магия;
/// - шина World с реверберацией: звуки мира отражаются от стен, интерфейс остаётся сухим.
///
/// Функция волны вызывается по порядку от начала звука к концу, поэтому у неё может быть
/// своё состояние (фильтры). Звук описывается «фабрикой»: она создаёт новую функцию
/// на каждый вариант, так что варианты шагов и ударов получаются разными.
/// </summary>
public partial class SoundManager : Node
{
    private const int MixRate = 44100;    // сэмплов в секунду
    private const int PoolSize = 16;      // сколько звуков может звучать одновременно
    private const float PeakLevel = 0.9f; // до какого уровня нормализуем каждый звук
    private const string WorldBus = "World";
    public const string MusicBus = "Music";
    public const string SoundsBus = "Sounds";
    private const float MusicVolumeDb = -11f;  // музыка — фон: тише звуков, ползунок лишь уменьшает её от этого уровня
    private const float AmbienceSeconds = 12f;
    private const float AmbienceVolumeDb = -24f;

    private sealed record SoundDef(List<AudioStreamWav> Variants, float VolumeDb, float PitchSpread, bool World);

    private readonly Dictionary<Sound, SoundDef> _sounds = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private readonly Random _noise = new();
    private AudioStreamPlayer _ambience;
    private int _nextPlayer;
    private double _dripTimer = 6.0;

    /// <summary>Простейший фильтр: пропускает частоты ниже cutoff, срез можно менять на ходу.</summary>
    private sealed class LowPass
    {
        private float _value;

        public float Process(float input, float cutoff)
        {
            float amount = 1f - Mathf.Exp(-Mathf.Tau * cutoff / MixRate);
            _value += (input - _value) * amount;
            return _value;
        }
    }

    /// <summary>Полосовой фильтр: разность двух фильтров оставляет только полосу между срезами.</summary>
    private sealed class BandPass
    {
        private readonly LowPass _high = new();
        private readonly LowPass _low = new();

        public float Process(float input, float from, float to) => _high.Process(input, to) - _low.Process(input, from);
    }

    public override void _Ready()
    {
        CreateWorldBus();
        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _players.Add(player);
        }

        AddImpacts();
        AddItems();
        AddMagic();
        AddWorld();
    }

    /// <summary>Фон: гул подземелья сразу, музыка — когда досчитается в отдельном потоке.</summary>
    public void StartBackground()
    {
        StartAmbience();
        Task.Run(() =>
        {
            var calm = ToBytes(MusicSynth.Render());
            var battle = ToBytes(MusicSynth.Render(battle: true));
            Callable.From(() => StartMusic(calm, battle)).CallDeferred(); // узлы трогаем только из главного потока
        });
    }

    private AudioStreamPlayer _calmMusic;
    private AudioStreamPlayer _battleMusic;
    private bool _battle;

    /// <summary>Босс заметил отряд: спокойная петля уходит, боевая проявляется. Обе идут вместе, поэтому в такт.</summary>
    public void SetBattle(bool battle)
    {
        if (battle == _battle || _calmMusic == null)
            return;
        _battle = battle;
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_calmMusic, "volume_db", battle ? SilentDb : MusicVolumeDb, 1.5f);
        tween.TweenProperty(_battleMusic, "volume_db", battle ? MusicVolumeDb : SilentDb, 1.5f);
    }

    private const float SilentDb = -50f;

    private void StartMusic(byte[] data, byte[] battle)
    {
        var player = new AudioStreamPlayer { Stream = ToMusicStream(data), Bus = MusicBus, VolumeDb = -40f };
        AddChild(player);
        player.Play();
        _calmMusic = player;
        _battleMusic = new AudioStreamPlayer { Stream = ToMusicStream(battle), Bus = MusicBus, VolumeDb = SilentDb };
        AddChild(_battleMusic);
        _battleMusic.Play();
        GD.Print($"Музыка: запущена, {data.Length / 1024 / 1024} МБ");
        // Музыка плавно проявляется, а не включается щелчком
        CreateTween().TweenProperty(player, "volume_db", MusicVolumeDb, 4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    private static AudioStreamWav ToMusicStream(byte[] data) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = MusicSynth.MixRate,
        Stereo = true,
        Data = data,
        LoopMode = AudioStreamWav.LoopModeEnum.Forward,
        LoopBegin = 0,
        LoopEnd = data.Length / 4, // в кадрах: два канала по два байта
    };

    /// <summary>Реверберация — отражения от стен. Отдельная шина, чтобы интерфейс звучал сухо.</summary>
    private static void CreateWorldBus()
    {
        // Все звуки сходятся в шину Sounds: у неё свой ползунок, а эхо мира идёт через неё
        if (AudioServer.GetBusIndex(SoundsBus) < 0)
        {
            AudioServer.AddBus();
            int sounds = AudioServer.BusCount - 1;
            AudioServer.SetBusName(sounds, SoundsBus);
            AudioServer.SetBusSend(sounds, "Master");
        }
        if (AudioServer.GetBusIndex(MusicBus) < 0)
        {
            // У музыки своя шина: её громкость настраивается отдельно от звуков
            AudioServer.AddBus();
            int music = AudioServer.BusCount - 1;
            AudioServer.SetBusName(music, MusicBus);
            AudioServer.SetBusSend(music, "Master");
            AudioServer.AddBusEffect(music, new AudioEffectReverb { RoomSize = 0.8f, Damping = 0.6f, Wet = 0.3f, Dry = 1f, Spread = 0.9f });
        }
        Settings.ApplyBusVolumes();
        if (AudioServer.GetBusIndex(WorldBus) >= 0)
            return;
        AudioServer.AddBus();
        int bus = AudioServer.BusCount - 1;
        AudioServer.SetBusName(bus, WorldBus);
        AudioServer.SetBusSend(bus, SoundsBus);
        AudioServer.AddBusEffect(bus, new AudioEffectReverb
        {
            RoomSize = 0.62f,
            Damping = 0.55f,
            PredelayMsec = 28f,
            Wet = 0.24f,
            Dry = 1f,
            Spread = 0.6f,
        });
    }

    // ---------- Бой ----------

    private void AddImpacts()
    {
        // Шаг: глухой тычок подошвы о камень
        Add(Sound.Step, 0.09f, -21f, () =>
        {
            var filter = new LowPass();
            float cutoff = (float)GD.RandRange(500.0, 900.0);
            return t => filter.Process(Noise(), cutoff) * Decay(t, 50f) * 2f + Sine(Chirp(120f, 62f, 0.09f, t)) * 0.5f * Decay(t, 38f);
        }, variants: 4, pitch: 0.06f);

        // Удар: хлёсткое начало, тяжёлое «тело» и еле слышный звон оружия
        Add(Sound.Hit, 0.2f, -8f, () =>
        {
            var filter = new LowPass();
            float clang = (float)GD.RandRange(1700.0, 2300.0);
            return t => filter.Process(Noise(), 2600f) * Decay(t, 42f) * 1.3f
                        + Sine(Chirp(200f, 60f, 0.2f, t)) * 0.85f * Decay(t, 17f)
                        + Metal(clang, t, 30f) * 0.12f;
        }, variants: 4, pitch: 0.05f);

        // Смерть врага: падение тела — низкий спад и оседающий шум
        Add(Sound.Kill, 0.45f, -8f, () =>
        {
            var filter = new LowPass();
            return t => Sine(Chirp(240f, 42f, 0.45f, t)) * 0.8f * Decay(t, 7f)
                        + filter.Process(Noise(), 450f + 1600f * Decay(t, 11f)) * 1.1f * Decay(t, 10f);
        }, variants: 3);

        // Урон по герою: глухой удар и хриплый «выдох» (пила через фильтр похожа на голос)
        Add(Sound.Hurt, 0.26f, -9f, () =>
        {
            var filter = new LowPass();
            return t => filter.Process(Saw(Chirp(175f, 92f, 0.26f, t)), 950f) * 1.1f * Decay(t, 9f)
                        + Sine(92f, t) * 0.6f * Decay(t, 24f);
        }, variants: 3, pitch: 0.06f);

        // Выстрел: щелчок тетивы и шелест летящей стрелы
        Add(Sound.Shoot, 0.24f, -11f, () =>
        {
            var string1 = new LowPass();
            var air = new BandPass();
            return t => string1.Process(Saw(Chirp(230f, 165f, 0.24f, t)), 1500f) * 0.9f * Decay(t, 28f)
                        + air.Process(Noise(), 500f + 1200f * t / 0.24f, 2800f) * Mathf.Min(1f, t * 70f) * Decay(t, 13f) * 0.5f;
        }, variants: 3, pitch: 0.05f);
    }

    // ---------- Предметы ----------

    private void AddItems()
    {
        // Подбор: два лёгких металлических «тин-тин»
        Add(Sound.Pickup, 0.32f, -13f, () => t =>
            Metal(1568f, t, 20f) * 0.6f + Later(t, 0.07f, late => Metal(2349f, late, 17f) * 0.5f), pitch: 0.02f);

        // Лечение: тёплый аккорд, ноты вступают одна за другой
        Add(Sound.Heal, 0.85f, -12f, () => t =>
            Pad(330f, t, 0f) + Pad(440f, t, 0.07f) + Pad(554f, t, 0.14f) + Pad(659f, t, 0.21f), pitch: 0.01f);

        // Надеть вещь: шорох кожи и короткий лязг пряжки
        Add(Sound.Equip, 0.28f, -12f, () =>
        {
            var filter = new LowPass();
            return t => filter.Process(Noise(), 1300f) * Decay(t, 30f) * 1.2f
                        + Metal(1150f, t, 22f) * 0.35f
                        + Later(t, 0.05f, late => Metal(1730f, late, 26f) * 0.3f);
        }, variants: 2, pitch: 0.03f);

        // Сундук: щелчок замка и рассыпавшиеся монеты (у каждого варианта свои времена и высоты)
        Add(Sound.Chest, 0.75f, -12f, () =>
        {
            var filter = new LowPass();
            var coins = Enumerable.Range(0, 9)
                .Select(i => (Start: 0.07f + i * 0.055f + (float)GD.RandRange(0.0, 0.03), Pitch: (float)GD.RandRange(2100.0, 3900.0)))
                .ToArray();
            return t => filter.Process(Noise(), 2200f) * Decay(t, 130f) * 0.5f
                        + coins.Sum(coin => Later(t, coin.Start, late => Metal(coin.Pitch, late, 32f) * 0.28f));
        }, variants: 2, pitch: 0.02f);
    }

    // ---------- Магия ----------

    private void AddMagic()
    {
        // Огонь: нарастающий рёв шума с редким треском и низким гулом
        Add(Sound.Fire, 0.9f, -9f, () =>
        {
            var flame = new LowPass();
            var crackle = new BandPass();
            return t =>
            {
                float cutoff = 300f + 2300f * Mathf.Sin(Mathf.Pi * Mathf.Min(t / 0.9f, 1f));
                float spark = _noise.NextDouble() < 0.004 ? Noise() : 0f;
                return flame.Process(Noise(), cutoff) * 1.7f * Mathf.Min(1f, t * 22f) * Decay(t, 3.2f)
                       + crackle.Process(spark, 900f, 4000f) * 0.5f * Decay(t, 2.5f)
                       + Sine(Chirp(75f, 45f, 0.9f, t)) * 0.35f * Decay(t, 4f);
            };
        }, variants: 2);

        // Телепорт и лёд: стеклянный взлёт с мерцанием
        Add(Sound.Teleport, 0.6f, -12f, () => t =>
        {
            float swell = Mathf.Sin(Mathf.Pi * Mathf.Min(t / 0.6f, 1f));
            return (Sine(Chirp(260f, 1900f, 0.6f, t) + 3f * Sine(37f, t)) * 0.5f
                    + Sine(Chirp(520f, 3800f, 0.6f, t)) * 0.18f) * swell;
        }, pitch: 0.03f);

        // Новый уровень: светлое арпеджио на колоколах, ноты дозвучивают друг на друга
        Add(Sound.LevelUp, 1.2f, -9f, () => t =>
            Bell(523f, t, 4f) + Later(t, 0.1f, late => Bell(659f, late, 4f))
            + Later(t, 0.2f, late => Bell(784f, late, 4f)) + Later(t, 0.3f, late => Bell(1047f, late, 3f)), pitch: 0f);

        // Призыв: тёмный гул расстроенных низких нот, который раскрывается и гаснет
        Add(Sound.Summon, 0.95f, -9f, () =>
        {
            var filter = new LowPass();
            return t =>
            {
                float drone = Saw(Mathf.Tau * 55f * t) + Saw(Mathf.Tau * 55.8f * t) + Saw(Mathf.Tau * 82.5f * t);
                return filter.Process(drone, 180f + 1000f * t / 0.95f) * 0.55f * Mathf.Sin(Mathf.Pi * Mathf.Min(t / 0.95f, 1f));
            };
        }, pitch: 0.02f);

        // Гибель героя: низкий погребальный колокол и уходящий вниз тон
        Add(Sound.Death, 1.8f, -6f, () => t =>
            Bell(110f, t, 2.2f) * 1.4f + Sine(Chirp(220f, 52f, 1.8f, t)) * 0.3f * Decay(t, 2f), pitch: 0f);
    }

    // ---------- Подземелье и интерфейс ----------

    private void AddWorld()
    {
        // Лестница: три шага вниз по камню
        Add(Sound.Stairs, 0.55f, -11f, () =>
        {
            var filter = new LowPass();
            float Tread(float time, float pitch) =>
                Sine(Chirp(pitch * 2f, pitch, 0.2f, Mathf.Min(time, 0.2f))) * Decay(time, 20f) * 0.8f
                + filter.Process(Noise(), 700f) * Decay(time, 40f);
            return t => Tread(t, 90f) + Later(t, 0.15f, late => Tread(late, 76f) * 0.85f)
                        + Later(t, 0.3f, late => Tread(late, 64f) * 0.7f);
        }, pitch: 0.03f);

        // Дверь: скрип петель (пила в узкой полосе, «дрожащая» по громкости) и стук о стену
        Add(Sound.Door, 0.55f, -9f, () =>
        {
            var hinge = new BandPass();
            return t => hinge.Process(Saw(Chirp(70f, 125f, 0.55f, t)), 450f, 1500f) * 3f
                        * (0.55f + 0.45f * Mathf.Sin(t * 85f)) * Mathf.Sin(Mathf.Pi * Mathf.Min(t / 0.45f, 1f))
                        + Later(t, 0.42f, late => Sine(82f, late) * 0.35f * Decay(late, 22f));
        }, variants: 3, pitch: 0.06f);

        // Секрет: скрежет камня и загадочные минорные ноты
        Add(Sound.Secret, 0.95f, -11f, () =>
        {
            var stone = new LowPass();
            return t => stone.Process(Noise(), 380f) * 1.4f * Mathf.Sin(Mathf.Pi * Mathf.Min(t / 0.5f, 1f))
                        + Later(t, 0.2f, late => Bell(220f, late, 4f) * 0.6f)
                        + Later(t, 0.35f, late => Bell(262f, late, 4f) * 0.6f)
                        + Later(t, 0.5f, late => Bell(330f, late, 3.5f) * 0.6f);
        }, pitch: 0.01f);

        // Капля воды где-то вдали: частота быстро взлетает, как у настоящей капли
        Add(Sound.Drip, 0.14f, -27f, () => t => Sine(Chirp(900f, 2300f, 0.14f, t)) * Decay(t, 34f), variants: 3, pitch: 0.12f);

        // Интерфейс: сухой деревянный «тик» при выборе и щелчок при нажатии
        Add(Sound.UiHover, 0.04f, -27f, () => t => Sine(1250f, t) * Decay(t, 110f), pitch: 0.03f, world: false);
        Add(Sound.UiClick, 0.09f, -17f, () =>
        {
            var filter = new LowPass();
            return t => filter.Process(Noise(), 3000f) * Decay(t, 130f) * 1.2f + Sine(Chirp(540f, 380f, 0.09f, t)) * 0.7f * Decay(t, 45f);
        }, pitch: 0.03f, world: false);
    }

    /// <summary>true — звуки не играются (быстрая симуляция).</summary>
    public bool Muted { get; set; }

    public void Play(Sound sound)
    {
        if (Muted || !_sounds.TryGetValue(sound, out var def))
            return;

        // Берём свободный плеер, а если все заняты — по кругу перебиваем самый старый
        var player = _players.Find(p => !p.Playing) ?? _players[_nextPlayer++ % PoolSize];
        player.Stream = def.Variants[_noise.Next(def.Variants.Count)];
        player.Bus = def.World ? WorldBus : SoundsBus;
        player.VolumeDb = def.VolumeDb;
        player.PitchScale = 1f + (float)GD.RandRange(-def.PitchSpread, def.PitchSpread); // чтобы повторы не надоедали
        player.Play();
    }

    /// <summary>Редкие капли воды: время от времени, всегда в разный момент.</summary>
    public override void _Process(double delta)
    {
        if (_ambience == null || Muted)
            return;
        _dripTimer -= delta;
        if (_dripTimer > 0.0)
            return;
        _dripTimer = GD.RandRange(5.0, 16.0);
        Play(Sound.Drip);
    }

    /// <summary>Описать звук. factory создаёт функцию волны заново для каждого варианта.</summary>
    private void Add(Sound sound, float duration, float volumeDb, Func<Func<float, float>> factory,
        int variants = 1, float pitch = 0.04f, bool world = true)
    {
        var streams = Enumerable.Range(0, variants).Select(_ => Synthesize(duration, factory())).ToList();
        _sounds[sound] = new SoundDef(streams, volumeDb, pitch, world);
    }

    private static AudioStreamWav Synthesize(float duration, Func<float, float> wave)
    {
        int count = (int)(duration * MixRate);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            // Короткое затухание в самом конце убирает щелчок от резкого обрыва
            float fadeOut = Mathf.Min(1f, (count - i) / (MixRate * 0.01f));
            samples[i] = wave((float)i / MixRate) * fadeOut;
        }
        Normalize(samples);
        return ToStream(samples);
    }

    /// <summary>Подгоняем пик к одному уровню: громкость звука задаёт только его значение в дБ.</summary>
    private static void Normalize(float[] samples)
    {
        float peak = samples.Max(Mathf.Abs);
        if (peak <= 0f)
            return;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= PeakLevel / peak;
    }

    /// <summary>Переводим числа в байты 16-битного звука (little-endian: младший байт первым).</summary>
    private static AudioStreamWav ToStream(float[] samples) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = MixRate,
        Stereo = false,
        Data = ToBytes(samples),
    };

    private static byte[] ToBytes(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(sample & 0xFF);
            data[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        return data;
    }

    /// <summary>
    /// Фон подземелья: низкий шум, похожий на сквозняк, с медленными волнами громкости.
    /// Запись зациклена: конец плавно перетекает в начало, поэтому стыка не слышно,
    /// а частоты волн кратны длине петли и в начале и конце совпадают.
    /// </summary>
    private void StartAmbience()
    {
        int loop = (int)(AmbienceSeconds * MixRate);
        int overlap = MixRate * 2;
        var raw = new float[loop + overlap];
        var wind = new LowPass();
        var rumble = new LowPass();
        for (int i = 0; i < raw.Length; i++)
        {
            float t = (float)i / MixRate;
            float swell = 0.65f + 0.35f * Mathf.Sin(Mathf.Tau * t * 2f / AmbienceSeconds);
            raw[i] = wind.Process(Noise(), 260f + 90f * Mathf.Sin(Mathf.Tau * t * 3f / AmbienceSeconds)) * swell
                     + rumble.Process(Noise(), 70f) * 2.2f;
        }

        var samples = new float[loop];
        for (int i = 0; i < loop; i++)
        {
            float mix = i < overlap ? (float)i / overlap : 1f;
            samples[i] = i < overlap ? raw[i] * mix + raw[loop + i] * (1f - mix) : raw[i];
        }
        Normalize(samples);

        var stream = ToStream(samples);
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        stream.LoopBegin = 0;
        stream.LoopEnd = loop;
        _ambience = new AudioStreamPlayer { Stream = stream, VolumeDb = AmbienceVolumeDb, Bus = WorldBus };
        AddChild(_ambience);
        _ambience.Play();
    }

    // ---------- Проверка ----------

    /// <summary>Подпись для демонстрации: все звуки подряд с их названиями.</summary>
    public static IEnumerable<Sound> All => Enum.GetValues<Sound>();

    /// <summary>
    /// Числа о звуках — чтобы проверить их без ушей. Пик — громкость вершины, средняя — общая
    /// плотность, хвост — уровень в конце (должен быть почти 0, иначе будет щелчок), смещение —
    /// постоянная составляющая (должна быть около 0), яркость — грубая оценка высоты звука в Гц.
    /// </summary>
    public IEnumerable<string> Report()
    {
        foreach (string name in new[] { WorldBus, SoundsBus, MusicBus })
            yield return $"ШИНА: {name} идёт в {AudioServer.GetBusSend(AudioServer.GetBusIndex(name))}, громкость {AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(name)):0.0} дБ";
        foreach (var (sound, def) in _sounds)
        {
            foreach (var stream in def.Variants.Take(1))
            {
                var data = stream.Data;
                var samples = new float[data.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
                float rms = Mathf.Sqrt(samples.Average(s => s * s));
                float tail = samples.TakeLast(MixRate / 500).Max(Mathf.Abs);
                int crossings = samples.Zip(samples.Skip(1)).Count(pair => pair.First < 0f != pair.Second < 0f);
                float brightness = crossings * MixRate / 2f / samples.Length;
                yield return $"ЗВУК: {sound,-9} {samples.Length / (float)MixRate:0.00} с  {def.VolumeDb,4:0} дБ  вариантов {def.Variants.Count}  " +
                             $"пик {samples.Max(Mathf.Abs):0.00}  средняя {rms:0.000}  хвост {tail:0.000}  смещение {samples.Average():0.000}  яркость {brightness:0} Гц";
            }
        }
    }

    /// <summary>Записать один вариант каждого звука в WAV — их можно послушать в обычном плеере.</summary>
    public void ExportWav(string folder)
    {
        foreach (var (sound, def) in _sounds)
            def.Variants[0].SaveToWav($"{folder}/{sound}.wav");
        var music = MusicSynth.Render();
        foreach (var line in MusicSynth.Report(music))
            GD.Print(line);
        ToMusicStream(ToBytes(music)).SaveToWav($"{folder}/Music.wav");
    }

    // ---------- Кирпичики для звуков ----------

    private float Noise() => (float)(_noise.NextDouble() * 2.0 - 1.0);

    private static float Decay(float t, float speed) => Mathf.Exp(-t * speed);

    private static float Sine(float frequency, float t) => Mathf.Sin(Mathf.Tau * frequency * t);

    /// <summary>Синус от готовой фазы (для тонов с меняющейся частотой).</summary>
    private static float Sine(float phase) => Mathf.Sin(phase);

    /// <summary>Пила: богата обертонами, через фильтр звучит как струна или голос.</summary>
    private static float Saw(float phase)
    {
        float cycles = phase / Mathf.Tau;
        return 2f * (cycles - Mathf.Floor(cycles)) - 1f;
    }

    /// <summary>
    /// Фаза тона, частота которого плавно меняется от f0 до f1 за duration секунд.
    /// Фаза — это интеграл частоты: 2π·(f0·t + (f1 − f0)·t² / (2·duration)).
    /// </summary>
    private static float Chirp(float f0, float f1, float duration, float t) =>
        Mathf.Tau * (f0 * t + (f1 - f0) * t * t / (2f * duration));

    /// <summary>Часть звука, которая вступает не сразу, а через start секунд.</summary>
    private static float Later(float t, float start, Func<float, float> part) => t < start ? 0f : part(t - start);

    /// <summary>Металл: кратности обертонов нецелые, поэтому слух не складывает их в одну ноту.</summary>
    private static float Metal(float frequency, float t, float decay) =>
        (Sine(frequency, t) + Sine(frequency * 1.47f, t) * 0.6f + Sine(frequency * 2.09f, t) * 0.35f)
        * 0.5f * Decay(t, decay) * Mathf.Min(1f, t * 3000f);

    /// <summary>Колокол: обертоны почти кратные, верхние гаснут быстрее нижних — поэтому звук теплеет.</summary>
    private static float Bell(float frequency, float t, float decay) =>
        (Sine(frequency, t) * Decay(t, decay)
         + Sine(frequency * 2.01f, t) * 0.4f * Decay(t, decay * 1.7f)
         + Sine(frequency * 3.02f, t) * 0.15f * Decay(t, decay * 2.6f))
        * 0.45f * Mathf.Min(1f, t * 800f);

    /// <summary>Мягкая нота с плавным вступлением и угасанием — из них складывается аккорд лечения.</summary>
    private static float Pad(float frequency, float t, float start) => Later(t, start, late =>
        (Sine(frequency, late) + Sine(frequency * 2f, late) * 0.2f) * 0.3f * Mathf.Min(1f, late * 16f) * Decay(late, 3.4f));
}
