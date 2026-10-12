namespace Sacred.Network.TinCat;

/// <summary>TinCat's reflected CRC32 with initial zero and no final XOR (0x1000EA10).</summary>
public static class TinCatChecksum
{
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint result = 0;
        foreach (var value in bytes)
        {
            result ^= value;
            for (var bit = 0; bit < 8; bit++)
                result = (result >> 1) ^ ((result & 1) != 0 ? 0xEDB88320u : 0);
        }
        return result;
    }
}
