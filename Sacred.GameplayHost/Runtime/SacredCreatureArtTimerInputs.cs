using Sacred.Assets.Executable;

namespace Sacred.GameplayHost.Runtime;

/// <summary>Saved acquired-art sources and original definitions for a private
/// calculation. A fresh work list on each call preserves retry determinism.</summary>
public sealed class SacredCreatureArtTimerInputs
{
    private readonly byte[][] _records;
    internal SacredCombatArtTimerDefinitions Definitions { get; }
    internal SacredCombatArtSkillTimers Skills { get; }
    public SacredCreatureArtTimerInputs(IEnumerable<byte[]> records, SacredCombatArtTimerDefinitions definitions, SacredCombatArtSkillTimers skills)
        => (_records, Definitions, Skills) = (records.Select(row => row.ToArray()).ToArray(), definitions, skills);
    public int Count => _records.Length;
    public SacredCreatureArtTimerInputs WithRecords(IEnumerable<byte[]> records) => new(records, Definitions, Skills);
    internal SacredCreatureArtTimers CreateCalculation() => new(_records, Definitions, Skills);
}
