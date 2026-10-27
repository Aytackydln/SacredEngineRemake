namespace Sacred.Core.Runtime;

/// <summary>Gold 0x443B20: signed endpoints with asymmetric negative defaults,
/// endpoint swapping, inclusive draw, and no RNG consumption on equality.</summary>
public static class SacredRandomRange
{
    public static int Inclusive(int minimum, int maximum, SacredRandomSequence random)
    {
        if (minimum < 0) minimum = 0;
        if (maximum < 0) maximum = 1;
        if (minimum == maximum) return minimum;
        if (maximum < minimum) (minimum, maximum) = (maximum, minimum);
        return unchecked(random.Next() % (maximum - minimum + 1) + minimum);
    }
}
