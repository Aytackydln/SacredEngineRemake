namespace Sacred.GameplayHost.Scripting;
/// <summary>Additional SetVar/AddVar writes to Gold's 150-word interpreter array at +0x7750.
/// Initialization is a separate world dependency; these writes cannot invent its previous values.</summary>
public sealed class WorldScriptVariableGlobals
{
    public const int ValueCount = 150;
    private const string Prefix = "atmo_rg";
    private readonly List<WorldScriptVariableGlobalMutation> _mutations = [];
    public WorldScriptVariableGlobals() => Mutations = _mutations.AsReadOnly();
    public IReadOnlyList<WorldScriptVariableGlobalMutation> Mutations { get; }
    public int UnresolvedIndexCount => _mutations.Count(mutation => mutation.Index is null);
    internal void Record(string name, int value, bool increment)
    {
        // Preserve the original inequality in 0x49B2B0/0x49C930: matching prefixes skip this write.
        if (name.Length >= Prefix.Length && WorldScriptNameComparer.Instance.Equals(name[..Prefix.Length], Prefix)) return;
        int? index = name.Length < Prefix.Length ? null : Math.Clamp(Atoi(name.AsSpan(Prefix.Length)), 0, ValueCount - 1);
        _mutations.Add(new(name, index, increment ? 1 : value, increment));
    }
    /// <summary>Apply once to an established initial array. Short names cause native reads after their NUL;
    /// without known name-buffer storage, their affected index stays unresolved.</summary>
    public int[] Apply(IReadOnlyList<int> initial, Func<string, int?>? resolveShortNameIndex = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (initial.Count != ValueCount) throw new ArgumentException("Native variable globals require 150 established values.", nameof(initial));
        var result = initial.ToArray();
        foreach (var mutation in _mutations)
        {
            var index = mutation.Index ?? resolveShortNameIndex?.Invoke(mutation.Name) ??
                throw new InvalidDataException($"Variable '{mutation.Name}' requires established native name-buffer suffix bytes for its global write.");
            if ((uint)index >= ValueCount) throw new InvalidDataException("Resolved variable global index exceeds 149.");
            result[index] = mutation.Increment ? unchecked(result[index] + 1) : mutation.Value;
        }
        return result;
    }
    private static int Atoi(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while (offset < text.Length && (text[offset] == ' ' || text[offset] is >= '\t' and <= '\r')) offset++;
        var negative = offset < text.Length && text[offset] == '-';
        if (offset < text.Length && text[offset] is '+' or '-') offset++;
        var value = 0;
        while (offset < text.Length && text[offset] is >= '0' and <= '9') value = unchecked(value * 10 + text[offset++] - '0');
        return negative ? unchecked(-value) : value;
    }
}
