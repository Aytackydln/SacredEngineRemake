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
    /// <summary>Gold 0x6224E0: signed integer world coordinate divided by scale, truncated toward zero.</summary>
    public static int WorldToTile(int world) => (int)(((long)world << FractionBits) / Numerator);
}
