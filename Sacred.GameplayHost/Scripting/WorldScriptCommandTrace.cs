using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>An observed command attempt, including predicates and the active function path.
/// This describes managed execution order, not native stack provenance or completed effects.</summary>
public sealed record WorldScriptCommandTrace(string Script, SacredScriptCommand Command,
    IReadOnlyList<string> CallPath, bool IsPredicate);
