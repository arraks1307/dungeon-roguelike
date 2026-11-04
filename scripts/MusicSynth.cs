using System;
using System.Collections.Generic;

namespace Roguelike;

/// <summary>
/// Фоновая музыка, сочинённая кодом: без аудиофайлов, как и остальные звуки.
/// Спокойная петля в ля миноре: протяжные аккорды, низкий бас и редкая мелодия колокольчиков с эхом.
///
/// Запись стереофоническая и зациклена без стыка: ноты пишутся в кольцевой буфер, поэтому
/// хвост последнего аккорда сам заворачивается в начало петли.
/// Здесь нет обращений к движку: считается в отдельном потоке и не задерживает запуск игры.
/// </summary>
public static class MusicSynth
{
    public const int MixRate = 32000; // верхних частот в музыке нет, а памяти нужно меньше, чем при 44 кГц

    private const float ChordSeconds = 6f;
    private const float MelodySlotSeconds = 1.5f;
    private const float EchoSeconds = 0.75f;
    private const int MelodySeed = 1307; // одно и то же зерно — одна и та же мелодия при каждом запуске

    // Ноты — номера MIDI (60 — до первой октавы). Первая в аккорде — бас, остальные — средние голоса
    private static readonly int[][] Progression =
    {
        new[] { 33, 57, 60, 64 }, // Am
        new[] { 29, 57, 60, 65 }, // F
        new[] { 36, 55, 60, 64 }, // C
        new[] { 31, 55, 59, 62 }, // G
        new[] { 33, 57, 60, 64 }, // Am
        new[] { 38, 57, 62, 65 }, // Dm
        new[] { 29, 57, 60, 65 }, // F
        new[] { 28, 56, 59, 64 }, // E — напряжение, которое разрешается в начало следующей строки
        new[] { 33, 57, 60, 64 }, // Am
        new[] { 29, 57, 60, 65 }, // F
        new[] { 38, 57, 62, 65 }, // Dm
        new[] { 28, 56, 59, 64 }, // E
    };

    // Минорная пентатоника: в ней любые ноты звучат мягко поверх этих аккордов
    private static readonly int[] Melody = { 69, 72, 74, 76, 79, 81 };

    public static float LoopSeconds => Progression.Length * ChordSeconds;

    /// <summary>Готовая запись: чередуются левый и правый каналы, значения от -1 до 1.</summary>
    public static float[] Render()
    {
        int length = (int)(LoopSeconds * MixRate);
        var left = new float[length];
        var right = new float[length];

        for (int i = 0; i < Progression.Length; i++)
        {
            float start = i * ChordSeconds;
            var chord = Progression[i];
            AddNote(left, right, start, ChordSeconds * 1.2f, Frequency(chord[0]), 0.34f, 0f, Bass, Pad);
            for (int voice = 1; voice < chord.Length; voice++)
            {
                float pan = (voice - 2) * 0.45f; // голоса разнесены по ширине
                AddNote(left, right, start, ChordSeconds * 1.35f, Frequency(chord[voice]), 0.15f, pan, Warm, Pad);
            }
        }
        AddMelody(left, right);

        var samples = new float[length * 2];
        float peak = 0.0001f;
        for (int i = 0; i < length; i++)
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[i]), MathF.Abs(right[i])));
        for (int i = 0; i < length; i++)
        {
            samples[i * 2] = left[i] * 0.85f / peak;
            samples[i * 2 + 1] = right[i] * 0.85f / peak;
        }
        return samples;
    }

    /// <summary>
    /// Мелодия идёт ступеньками: следующая нота рядом с предыдущей, так она похожа на напев,
    /// а не на случайные звуки. Каждая третья строка почти молчит, чтобы музыка дышала.
    /// </summary>
    private static void AddMelody(float[] left, float[] right)
    {
        var rng = new Random(MelodySeed);
        int position = 2;
        int slots = (int)(LoopSeconds / MelodySlotSeconds);
        for (int slot = 0; slot < slots; slot++)
        {
            int chord = (int)(slot * MelodySlotSeconds / ChordSeconds);
            double chance = chord % 4 == 3 ? 0.15 : 0.5;
            if (rng.NextDouble() > chance)
                continue;
            position = Math.Clamp(position + rng.Next(-2, 3), 0, Melody.Length - 1);
            float start = slot * MelodySlotSeconds;
            float pan = (float)(rng.NextDouble() - 0.5) * 0.7f;

            // Эхо — та же нота позже и тише, с другой стороны
            float volume = 0.2f;
            for (int echo = 0; echo < 4; echo++)
            {
                AddNote(left, right, start + echo * EchoSeconds, 3.2f, Frequency(Melody[position]), volume, pan, Chime, Pluck);
                volume *= 0.42f;
                pan = -pan;
            }
        }
    }

    private static float Frequency(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    // Тембр — громкости обертонов: чем их меньше, тем мягче звук
    private static readonly float[] Bass = { 1f, 0.3f };
    private static readonly float[] Warm = { 1f, 0.28f, 0.07f };
    private static readonly float[] Chime = { 1f, 0.25f, 0.05f };

    /// <summary>Протяжная нота: медленно вступает и медленно гаснет, соседние аккорды перетекают.</summary>
    private static float Pad(float t, float length)
    {
        float rise = MathF.Min(t / 1.8f, 1f);
        float fall = MathF.Min((length - t) / 3f, 1f);
        return rise * rise * (3f - 2f * rise) * fall * fall * (3f - 2f * fall);
    }

    /// <summary>Щипок: быстрое мягкое вступление и долгое затухание.</summary>
    private static float Pluck(float t, float length) =>
        MathF.Min(t * 60f, 1f) * MathF.Exp(-t * 2.3f) * MathF.Min((length - t) * 4f, 1f);

    /// <summary>
    /// Записать ноту в кольцевой буфер. В правом канале частота чуть выше: два почти одинаковых
    /// тона медленно расходятся и сходятся, и нота «живёт», а не стоит на месте.
    /// </summary>
    private static void AddNote(float[] left, float[] right, float start, float length, float frequency,
        float volume, float pan, float[] timbre, Func<float, float, float> envelope)
    {
        int first = (int)(start * MixRate);
        int count = (int)(length * MixRate);
        float leftGain = volume * (1f - MathF.Max(pan, 0f));
        float rightGain = volume * (1f + MathF.Min(pan, 0f));
        float step = MathF.Tau * frequency / MixRate;

        for (int i = 0; i < count; i++)
        {
            float amplitude = envelope((float)i / MixRate, length);
            int index = (first + i) % left.Length;
            for (int h = 0; h < timbre.Length; h++)
            {
                float harmonic = h + 1;
                left[index] += MathF.Sin(step * harmonic * i) * timbre[h] * amplitude * leftGain;
                right[index] += MathF.Sin(step * 1.0025f * harmonic * i) * timbre[h] * amplitude * rightGain;
            }
        }
    }

    /// <summary>Числа для проверки: насколько громко и нет ли скачка на стыке петли.</summary>
    public static IEnumerable<string> Report(float[] samples)
    {
        float peak = 0f, sum = 0f, jump = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(samples[i]));
            sum += samples[i] * samples[i];
            if (i >= 2)
                jump = MathF.Max(jump, MathF.Abs(samples[i] - samples[i - 2]));
        }
        float seam = MathF.Max(MathF.Abs(samples[0] - samples[^2]), MathF.Abs(samples[1] - samples[^1]));
        yield return $"МУЗЫКА: {LoopSeconds:0} с  пик {peak:0.00}  средняя {MathF.Sqrt(sum / samples.Length):0.000}  " +
                     $"стык петли {seam:0.0000}  самый большой шаг внутри {jump:0.0000}";
    }
}
