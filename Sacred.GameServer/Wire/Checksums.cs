namespace Sacred.GameServer.Wire;

/// <summary>TinCat frame body checksum (reflected CRC-32 polynomial, no init/final xor). Table driven, allocation free.</summary>
internal static class TinCatCrc
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB88320u : 0);
            table[i] = value;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint result = 0;
        var table = Table;
        foreach (var value in bytes) result = (result >> 8) ^ table[(byte)(result ^ value)];
        return result;
    }
}

/// <summary>Sacred message/envelope rolling checksum; same algorithm as Sacred.Network.Protocol.SacredChecksum.</summary>
internal static class SacredCrc
{
    public static uint Compute(ReadOnlySpan<byte> bytes) => Compute(bytes, skipChecksumFields: false);

    /// <summary>Message checksum: header bytes 4..9 (checksum and sequence) count as zero.</summary>
    public static uint ComputeMessage(ReadOnlySpan<byte> message) => Compute(message, skipChecksumFields: true);

    private static uint Compute(ReadOnlySpan<byte> bytes, bool skipChecksumFields)
    {
        uint rolling = 0, result = 0x6F60093A, key = 0x3EF05ADB;
        var wordBytes = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = skipChecksumFields && i is >= 4 and < 10 ? (byte)0 : bytes[i];
            rolling = unchecked((rolling << 8) | value);
            wordBytes = (wordBytes + 1) & 3;
            if (wordBytes != 0) continue;
            result ^= key ^ rolling;
            key = unchecked((key ^ rolling) + 0x63FE53BA);
        }
        return bytes.IsEmpty ? 0 : result ^ rolling;
    }
}
