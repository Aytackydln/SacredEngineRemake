namespace Sacred.GameplayHost.Scripting;

/// <summary>Caller-owned inputs; difficulty comes from session flags, the
/// counter/reference pool and random sequence are shared with startup.</summary>
public sealed record WorldScriptCreatureRuntime(byte Owner, uint Difficulty, bool Owned, uint Players, Func<uint> Clock)
{
    public Sacred.Core.Runtime.SacredObjectProjection? EffectProjection { get; init; }
    /// <summary>Optional host DETAILLEVEL override. When absent, the original
    /// configuration constructor's executable-derived default is used.</summary>
    public byte? EffectDetailLevel { get; init; }
    /// <summary>Authoritative current hero/team/statistics/progress/sector snapshot
    /// for automatic requested levels. Presence does not resolve actor identity.
    /// The provider is queried at the native post-service level stage.</summary>
    public Func<ushort,WorldCreatureAutomaticLevelInputs>? AutomaticLevelInputs { get; init; }
}
