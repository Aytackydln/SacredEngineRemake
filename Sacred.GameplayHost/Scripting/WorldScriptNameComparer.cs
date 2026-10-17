namespace Sacred.GameplayHost.Scripting;

/// <summary>Native default-C-locale name equality: fold only ASCII A..Z, retain high bytes.</summary>
public sealed class WorldScriptNameComparer : IEqualityComparer<string>
{
    public static WorldScriptNameComparer Instance { get; } = new();
    private WorldScriptNameComparer() { }
    public bool Equals(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Length != y.Length) return false;
        for (var i = 0; i < x.Length; i++) if (Fold(x[i]) != Fold(y[i])) return false;
        return true;
    }
    public int GetHashCode(string value)
    {
        var hash = new HashCode();
        foreach (var character in value) hash.Add(Fold(character));
        return hash.ToHashCode();
    }
    private static char Fold(char value) => value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;
}
