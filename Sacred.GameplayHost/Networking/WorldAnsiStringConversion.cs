namespace Sacred.GameplayHost.Networking;

/// <summary>Compared complete Gold 0x6586E0 / 0x658510 wrappers. Code-page behavior
/// belongs to supplied Windows services, not a substitute .NET encoding.</summary>
public static class WorldAnsiStringConversion
{
    /// <summary>Null preserves the destination after a failed size query. A
    /// positive query performs conversion and scans its buffer despite a failed
    /// second call. A native scan beyond the allocation is explicitly guarded.</summary>
    public static ReadOnlyMemory<char>? Decode(WorldAnsiConversionServices services,
        Func<ReadOnlyMemory<byte>> readSource)
    {
        var count = services.Decode(readSource(), default);
        if (count == 0) return null;
        if (count < 0 || count == int.MaxValue) throw new NotSupportedException("Native conversion allocation extent is not representable.");
        var buffer = services.AllocateWide(count + 1);
        if (buffer.Length != count + 1) throw new InvalidOperationException("Native wide conversion allocation extent differs.");
        _ = services.Decode(readSource(), buffer[..count]);
        buffer.Span[count] = '\n';
        var end = buffer.Span.IndexOf('\0');
        if (end < 0) throw new InvalidOperationException("Native ANSI conversion scanned beyond its wide allocation.");
        return buffer[..end].ToArray();
    }

    public static WorldAnsiEncodedString Encode(WorldAnsiConversionServices services,
        Func<ReadOnlyMemory<char>> readSource)
    {
        var count = services.Encode(readSource(), default);
        if (count <= 0) return new(Array.Empty<byte>(), count);
        if (count == int.MaxValue) throw new NotSupportedException("Native conversion allocation extent is not representable.");
        var buffer = services.AllocateAnsi(count + 1);
        if (buffer.Length != count + 1) throw new InvalidOperationException("Native ANSI conversion allocation extent differs.");
        _ = services.Encode(readSource(), buffer);
        buffer.Span[count] = 0;
        var end = buffer.Span.IndexOf((byte)0);
        return new(buffer[..end].ToArray(), count);
    }
}
