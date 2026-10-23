using Sacred.Core.GameBin.Scripts;

namespace Sacred.Network.Gameplay;

/// <summary>Function names at a specific receipt boundary, even when the original campaign loads later.</summary>
public sealed class GoldScriptFunctionSnapshot
{
    private readonly Func<IReadOnlyList<SacredScriptFunctionDefinition>?> _baseline;
    private readonly IReadOnlyDictionary<uint, SacredScriptFunctionDefinition> _updates;
    internal GoldScriptFunctionSnapshot(Func<IReadOnlyList<SacredScriptFunctionDefinition>?> baseline,
        IReadOnlyDictionary<uint, SacredScriptFunctionDefinition> updates) => (_baseline, _updates) = (baseline, updates);
    public bool TryGetDefinitions(out IReadOnlyList<SacredScriptFunctionDefinition> definitions)
    {
        if (_baseline() is not { } source) { definitions = Array.Empty<SacredScriptFunctionDefinition>(); return false; }
        var copy = source.ToArray();
        foreach (var pair in _updates) if (pair.Key < copy.Length) copy[pair.Key] = pair.Value;
        definitions = Array.AsReadOnly(copy);
        return true;
    }
}
