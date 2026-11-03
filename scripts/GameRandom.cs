using System;

namespace Roguelike;

/// <summary>
/// Генератор случайных чисел игрового мира. В сетевой игре у всех компьютеров одинаковое
/// «зерно» (seed), поэтому они получают одну и ту же последовательность чисел и строят один
/// и тот же мир. Счётчик Calls входит в контрольную сумму: если на каком-то компьютере
/// случайное число взяли лишний раз, это сразу видно.
///
/// Алгоритм свой (SplitMix64), а не встроенный в System.Random: у встроенного нельзя ни
/// прочитать, ни задать внутреннее состояние, а нам нужно передать его по сети, когда хост
/// восстанавливает разошедшийся мир. Здесь всё состояние — одно 64-битное число.
/// </summary>
public class GameRandom : Random
{
    private ulong _state;

    public GameRandom() : this(Environment.TickCount ^ Guid.NewGuid().GetHashCode()) { }

    public GameRandom(int seed) : base(0)
    {
        _state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
    }

    public long Calls { get; private set; }
    public ulong State => _state;

    /// <summary>Генератор ровно в том же состоянии, что был у хоста (для восстановления мира).</summary>
    public static GameRandom FromState(ulong state, long calls) => new(0) { _state = state, Calls = calls };

    /// <summary>Один шаг SplitMix64: прибавляем константу и тщательно «перемешиваем» биты.</summary>
    private ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    // 53 бита — ровно столько помещается в мантиссу double, поэтому число получается равномерным в [0, 1)
    protected override double Sample() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public override double NextDouble()
    {
        Calls++;
        return Sample();
    }

    public override int Next()
    {
        Calls++;
        return (int)(NextULong() >> 33); // 31 бит: 0 … int.MaxValue
    }

    public override int Next(int maxValue)
    {
        if (maxValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue));
        Calls++;
        ulong value = NextULong();
        return maxValue <= 1 ? 0 : (int)(value % (ulong)maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue));
        Calls++;
        ulong value = NextULong();
        long range = (long)maxValue - minValue;
        return range <= 1 ? minValue : (int)(minValue + (long)(value % (ulong)range));
    }
}
