namespace Sacred.Core.World;

/// <summary>Exact integer conversions through Gold's IEEE-754 world scale. Integer
/// arithmetic preserves x87 truncation and low-word results at Int32 boundaries.</summary>
public static class SacredWorldCoordinates
{
    private static readonly uint ScaleBits = BitConverter.SingleToUInt32Bits(SacredSectorCoordinates.WorldUnitsPerTile);
    private static readonly long Numerator = (ScaleBits & 0x7FFFFF) | 0x800000;
    private static readonly int FractionBits = 150 - (int)(ScaleBits >> 23);
    /// <summary>Gold 0x482E96: truncate (tile + 0.5) * scale and retain the low Int32.</summary>
    public static int TileCenterToWorld(int tile) => unchecked((int)((2L * tile + 1) * Numerator / (1L << (FractionBits + 1))));
    /// <summary>Gold startup PC=24: FILD is exact, then FADD 0.5 and FMUL scale
    /// each round to single precision before the CRT truncates to a 64-bit integer.
    /// Keep the low Int32, as the native caller does. This can differ by one from
    /// the wider conversion, e.g. tile 3379.</summary>
    public static int TileCenterToWorldStartup(int tile)
    {
        var center = (float)(tile + 0.5d);
        var scaled = center * SacredSectorCoordinates.WorldUnitsPerTile;
        return unchecked((int)(long)scaled);
    }
    /// <summary>Gold 0x6224E0: signed integer world coordinate divided by scale, truncated toward zero.</summary>
    public static int WorldToTile(int world) => (int)(((long)world << FractionBits) / Numerator);
}
