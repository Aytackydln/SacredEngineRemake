namespace Sacred.Core.Runtime;

/// <summary>Original 0x803EA0 formatting removal used for roster identifiers.
/// Rules come from the executable; matching and lengths use raw UTF16 code units.</summary>
public sealed class SacredTextMarkup
{
    public SacredTextMarkup(IEnumerable<SacredTextMarkupRemoval> removals)
    {
        ArgumentNullException.ThrowIfNull(removals);
        var rules = removals.ToArray();
        if (rules.Any(rule => string.IsNullOrEmpty(rule.Prefix) || rule.RemovalLength <= 0))
            throw new ArgumentException("Text removal requires nonempty prefixes and positive lengths.", nameof(removals));
        Removals = Array.AsReadOnly(rules);
    }

    public IReadOnlyList<SacredTextMarkupRemoval> Removals { get; }

    public string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var rule in Removals)
        {
            while (text.Length >= rule.RemovalLength)
            {
                var at = text.IndexOf(rule.Prefix, StringComparison.Ordinal);
                if (at < 0) break;
                text = text.Remove(at, Math.Min(rule.RemovalLength, text.Length - at));
            }
        }
        return text;
    }
}
