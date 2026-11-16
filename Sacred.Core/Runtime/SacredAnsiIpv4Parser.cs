namespace Sacred.Core.Runtime;

/// <summary>Original 0x805250 dotted-address conversion. Requires three dots,
/// truncates each wrapping decimal result to a byte and ignores trailing text.
/// The result is first-octet-high UInt32, not a .NET IPAddress validation policy.</summary>
public static class SacredAnsiIpv4Parser
{
    public static uint Parse(ReadOnlySpan<byte> bytes, ReadOnlySpan<ushort> classes)
    {
        if (bytes.IsEmpty) return 0;
        var position = 0;
        uint result = 0;
        for (var octet = 0; octet < 4; octet++)
        {
            var value = SacredAnsiDecimalParser.Parse(bytes[position..], classes);
            result = (result << 8) | unchecked((byte)value);
            if (octet == 3) break;
            var dot = -1;
            for (var index = position; index < bytes.Length && bytes[index] != 0; index++)
                if (bytes[index] == 0x2e) { dot = index; break; }
            if (dot < 0) return 0;
            position = dot + 1;
        }
        return result;
    }
}
