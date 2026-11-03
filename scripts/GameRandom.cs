using System;

namespace Roguelike;

/// <summary>
/// Генератор случайных чисел игрового мира. В сетевой игре у всех компьютеров одинаковое
/// «зерно» (seed), поэтому они получают одну и ту же последовательность чисел и строят один
/// и тот же мир. Счётчик Calls входит в контрольную сумму: если на каком-то компьютере
/// случайное число взяли лишний раз, это сразу видно.
/// </summary>
public class GameRandom : Random
{
    public GameRandom() { }

    public GameRandom(int seed) : base(seed) { }

    public long Calls { get; private set; }

    public override int Next()
    {
        Calls++;
        return base.Next();
    }

    public override int Next(int maxValue)
    {
        Calls++;
        return base.Next(maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        Calls++;
        return base.Next(minValue, maxValue);
    }

    public override double NextDouble()
    {
        Calls++;
        return base.NextDouble();
    }
}
