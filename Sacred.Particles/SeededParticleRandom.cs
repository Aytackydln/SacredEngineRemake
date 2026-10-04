namespace Sacred.Particles;

/// <summary>Explicit state for the seeded System.Random compatibility stream.</summary>
public sealed class SeededParticleRandom : Random
{
    private const int Big = int.MaxValue;
    private readonly int[] _values = new int[56];
    private int _next, _nextPrime = 21;
    public SeededParticleRandom(int seed)
    {
        var subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
        var mj = 161803398 - subtraction;
        _values[55] = mj;
        var mk = 1;
        for (var i = 1; i < 55; i++)
        {
            var ii = 21 * i % 55;
            _values[ii] = mk;
            mk = mj - mk;
            if (mk < 0) mk += Big;
            mj = _values[ii];
        }
        for (var k = 0; k < 4; k++)
        for (var i = 1; i < 56; i++)
        {
            _values[i] -= _values[1 + (i + 30) % 55];
            if (_values[i] < 0) _values[i] += Big;
        }
    }
    protected override double Sample()
    {
        if (++_next >= 56) _next = 1;
        if (++_nextPrime >= 56) _nextPrime = 1;
        var result = _values[_next] - _values[_nextPrime];
        if (result == Big) result--;
        if (result < 0) result += Big;
        _values[_next] = result;
        return result * (1.0 / Big);
    }
    public override double NextDouble() => Sample();
    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (int)(Sample() * maxValue);
    }
    public int[] Capture() => [.. _values, _next, _nextPrime];
    public void Restore(ReadOnlySpan<int> state)
    {
        if (state.Length != 58 || state[56] is < 0 or > 55 || state[57] is < 0 or > 55)
            throw new ArgumentException("Invalid particle RNG state.");
        state[..56].CopyTo(_values); _next = state[56]; _nextPrime = state[57];
    }
}
