using Sacred.Core.Executable;

namespace Sacred.Core.Runtime;

/// <summary>Original per-fgets-chunk decisions. Comment checks use a trimmed
/// copy, while strtok operates on the original C-string buffer.</summary>
public static class SacredSettingsLineParser
{
    public static KeyValuePair<byte[], byte[]>? Parse(SacredSettingsFileDefinition definition,
        ReadOnlySpan<byte> chunk, ReadOnlySpan<byte> keyDelimiters)
    {
        var nul = chunk.IndexOf((byte)0); if (nul >= 0) chunk = chunk[..nul];
        var trimmed = Trim(chunk);
        if (trimmed.IsEmpty || definition.SkipPrefixes.Span.Contains(trimmed[0])) return null;
        keyDelimiters = CString(keyDelimiters);
        var begin = 0;
        while (begin < chunk.Length && keyDelimiters.Contains(chunk[begin])) begin++;
        var end = begin;
        while (end < chunk.Length && !keyDelimiters.Contains(chunk[end])) end++;
        if (begin == end || end == chunk.Length) return null;
        var key = chunk[begin..end]; begin = end + 1;
        var valueDelimiters = CString(definition.ValueDelimiters.Span);
        while (begin < chunk.Length && valueDelimiters.Contains(chunk[begin])) begin++;
        end = begin;
        while (end < chunk.Length && !valueDelimiters.Contains(chunk[end])) end++;
        if (begin == end) return null;
        // Native checks token lengths before trimming; empty trimmed keys/values
        // are still inserted when their original tokens were nonempty.
        return new(Trim(key).ToArray(), Trim(chunk[begin..end]).ToArray());
    }
    private static ReadOnlySpan<byte> CString(ReadOnlySpan<byte> value)
    {
        var at = value.IndexOf((byte)0); return at < 0 ? value : value[..at];
    }
    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        var begin = 0; var end = value.Length;
        while (begin < end && value[begin] is 0x20 or 9 or 13) begin++;
        while (end > begin && value[end - 1] is 0x20 or 9 or 13) end--;
        return value[begin..end];
    }
}
