namespace Sacred.Core.Runtime;

/// <summary>Original executable operands used by CreateNPC's automatic-level
/// caller. Balance.bin supplies the separate level and difficulty tables.</summary>
public sealed class SacredCreatureAutomaticLevelTuning(float timeDivisor, float timeLimit, float timeMultiplier,
    byte progressThreshold, IEnumerable<float> progressAdditions)
{
    public float TimeDivisor { get; } = timeDivisor;
    public float TimeLimit { get; } = timeLimit;
    public float TimeMultiplier { get; } = timeMultiplier;
    public byte ProgressThreshold { get; } = progressThreshold;
    public IReadOnlyList<float> ProgressAdditions { get; } = Array.AsReadOnly(progressAdditions.ToArray());
}
