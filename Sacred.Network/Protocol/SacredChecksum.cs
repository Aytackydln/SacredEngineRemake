namespace Sacred.Network.Protocol;

/// <summary>The Gold network checksum at GameServer 0x491500.</summary>
public static class SacredChecksum
{
    public static uint Compute(ReadOnlySpan<byte> bytes) => Compute(bytes, false);

    /// <summary>Hashes exactly the requested file prefix without loading the archive into memory.</summary>
    public static uint Compute(Stream stream, long byteCount)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        var state = new SacredChecksumState();
        Span<byte> buffer = stackalloc byte[4096];
        while (byteCount > 0)
        {
            var read = stream.Read(buffer[..(int)Math.Min(buffer.Length, byteCount)]);
            if (read == 0) throw new EndOfStreamException("Fingerprint prefix exceeds available file bytes.");
            state.Append(buffer[..read]);
            byteCount -= read;
        }
        return state.Value;
    }

    // The application checksum excludes both its stored checksum and sequence.
    internal static uint ComputeMessage(ReadOnlySpan<byte> bytes) => Compute(bytes, true);

    private static uint Compute(ReadOnlySpan<byte> bytes, bool message)
    {
        var state = new SacredChecksumState();
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = message && i is >= 4 and < 10 ? (byte)0 : bytes[i];
            state.Append(value);
        }
        return state.Value;
    }
}
