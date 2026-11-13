using System.Globalization;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x460720 expands variable concatenations against the ordered interpreter table.</summary>
public static class WorldScriptVariableNames
{
    public static string Expand(string text, Func<string, int?> variable)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(variable);
        while (text.IndexOf('+') is var start && start >= 0)
        {
            if (!text.AsSpan(start + 1).StartsWith("Var(", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsupported variable name expression '{text}'.");
            var end = text.IndexOf(')', start + 5);
            if (end < 0) throw new InvalidDataException($"Unterminated variable name expression '{text}'.");
            var value = variable(text[(start + 5)..end]);
            text = text[..start] + (value?.ToString(CultureInfo.InvariantCulture) ?? "") + text[(end + 1)..];
        }
        return text;
    }
}
