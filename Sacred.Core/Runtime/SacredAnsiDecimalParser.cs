namespace Sacred.Core.Runtime;

/// <summary>Original single-byte CRT 0x84B24A/0x84B1BF arithmetic. Character
/// classes are explicit current data, not .NET culture/Unicode classification.
/// Parsing ends at the first nondigit/NUL and wraps exactly at 32 bits.</summary>
public static class SacredAnsiDecimalParser
{
    public static int Parse(ReadOnlySpan<byte> bytes, ReadOnlySpan<ushort> classes)
    {
        if (classes.Length != 256 || (classes[0] & 0x0c) != 0)
            throw new NotSupportedException("Decimal parsing requires an established single-byte CRT table with a terminating NUL class.");
        var index = 0;
        while ((classes[Read(bytes, index)] & 8) != 0) index++;
        var value = Read(bytes, index++);
        var sign = value;
        if (value is 0x2d or 0x2b) value = Read(bytes, index++);
        uint number = 0;
        while ((classes[value] & 4) != 0)
        {
            number = unchecked(number * 10 + value - 0x30u);
            value = Read(bytes, index++);
        }
        return unchecked((int)(sign == 0x2d ? 0u - number : number));
    }

    private static byte Read(ReadOnlySpan<byte> bytes, int index) => index < bytes.Length ? bytes[index] : (byte)0;
}
