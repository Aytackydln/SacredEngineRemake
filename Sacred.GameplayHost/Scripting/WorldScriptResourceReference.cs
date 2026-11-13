using System.Globalization;
using System.Text;
using Sacred.Core.GameRes;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Variable-free Gold Res: numeric IDs and symbolic resource hashes.
/// Numeric parsing is bounded to complete UInt32 literals; other expression
/// forms retain a required native service boundary.</summary>
public static class WorldScriptResourceReference
{
    public static uint Resolve(string operand, GameResStore resources)
    {
        if (!operand.StartsWith("Res:", StringComparison.OrdinalIgnoreCase) || operand.Contains('$'))
            throw new InvalidDataException("Script resource operand requires a variable-free Res: reference.");
        var value = operand.AsSpan(4);
        if (!value.IsEmpty && value[0] is >= '0' and <= '9')
        {
            if (value.ContainsAnyExceptInRange('0', '9') ||
                !uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
                throw new InvalidDataException("Script numeric resource expression requires native integer parsing.");
            return numeric;
        }
        foreach (var character in value)
            if (character > byte.MaxValue) throw new InvalidDataException("Script resource name must retain original single-byte encoding.");
        var hash = SacredResourceHash.ComputeDefaultCLocale(Encoding.Latin1.GetBytes(value.ToString()));
        return hash != 0 && resources.Strings.ContainsKey(hash) ? hash | 0x80000000 : 0;
    }
}
