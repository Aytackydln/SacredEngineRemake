namespace Sacred.Core.Pak.Creature;

/// <summary>Gold 0x5370B0 integer NPC distance, including its coordinate limit,
/// 32-bit subtraction and native square-root iteration.</summary>
public static class SacredNpcDistance
{
    public static uint Between(int sourceX, int sourceY, int targetX, int targetY)
    {
        var x = unchecked(targetX - sourceX);
        var y = unchecked(targetY - sourceY);
        if (x is < -32000 or > 32000 || y is < -32000 or > 32000) return 32000;
        var remaining = unchecked(x * x + y * y);
        if (remaining < 0) return uint.MaxValue;
        var root = 0;
        for (var bit = 0x10000000; bit != 0; bit >>= 2)
        {
            var trial = bit + root;
            root >>= 1;
            if (trial > remaining) continue;
            remaining -= trial;
            root += bit;
        }
        if (root < remaining) root++;
        return (uint)root;
    }
}
