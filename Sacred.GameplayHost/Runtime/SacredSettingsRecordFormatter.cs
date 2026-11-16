namespace Sacred.GameplayHost.Runtime;

/// <summary>Compared narrow printf record format: %s and %% with three supplied
/// C-string arguments. Other format instructions require their own comparison.</summary>
public static class SacredSettingsRecordFormatter
{
    public static byte[] Format(ReadOnlySpan<byte> format, ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> delimiter, ReadOnlyMemory<byte> value)
    {
        ReadOnlyMemory<byte>[] arguments = [key, delimiter, value]; var argument = 0; var bytes = new List<byte>();
        for (var index = 0; index < format.Length && format[index] != 0; index++)
        {
            if (format[index] != '%') { bytes.Add(format[index]); continue; }
            if (++index == format.Length) throw new NotSupportedException("Incomplete original settings format.");
            if (format[index] == '%') { bytes.Add((byte)'%'); continue; }
            if (format[index] != 's' || argument >= arguments.Length) throw new NotSupportedException("Original settings format requires another comparison.");
            foreach (var item in arguments[argument++].Span) { if (item == 0) break; bytes.Add(item); }
        }
        return bytes.ToArray();
    }
}
