using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold SetVar/AddVar caller storage: a copied name and its inherited suffix,
/// distinct from the variable table's persisted name bytes.</summary>
internal static class WorldScriptVariableNameStorage
{
    public static int ResolveIndex(string name, ReadOnlySpan<byte> storage)
    {
        if (storage.Length != SacredScriptVariableGlobalsLayout.CallerNameSize)
            throw new InvalidDataException("Expected the complete native 32-byte caller name storage.");
        var end = storage.IndexOf((byte)0);
        if (end < 0 || Encoding.Latin1.GetString(storage[..end]) != name)
            throw new InvalidDataException("Caller name storage does not match this variable mutation.");
        var suffix = storage[SacredScriptVariableGlobalsLayout.IndexSuffixOffset..];
        var offset = 0;
        while (offset < suffix.Length && (suffix[offset] == ' ' || suffix[offset] is >= 9 and <= 13)) offset++;
        var negative = offset < suffix.Length && suffix[offset] == '-';
        if (offset < suffix.Length && suffix[offset] is (byte)'+' or (byte)'-') offset++;
        var value = 0;
        while (offset < suffix.Length && suffix[offset] is >= (byte)'0' and <= (byte)'9')
            value = unchecked(value * 10 + suffix[offset++] - '0');
        // An all-digit suffix requires bytes outside the supplied storage; do not truncate it.
        if (offset == suffix.Length) throw new InvalidDataException("Native atoi requires further caller-stack bytes.");
        return Math.Clamp(negative ? unchecked(-value) : value, 0, SacredScriptVariableGlobalsLayout.Count - 1);
    }
}
