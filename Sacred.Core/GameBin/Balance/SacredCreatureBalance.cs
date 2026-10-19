namespace Sacred.Core.GameBin.Balance;

public readonly record struct SacredCreatureHealthLevel(float Level, float Factor);

/// <summary>Immutable original tuning required by creature level/health initialization.</summary>
public sealed class SacredCreatureBalance
{
    public SacredCreatureBalance(IEnumerable<SacredCreatureHealthLevel> healthLevels,
        IEnumerable<float> healthDifficulties, SacredSkillCurveLayout vitality,
        IEnumerable<int> levelPercentAdditions, IEnumerable<int> levelAdditions)
    {
        HealthLevels = Array.AsReadOnly(healthLevels.ToArray());
        HealthDifficulties = Array.AsReadOnly(healthDifficulties.ToArray());
        LevelPercentAdditions = Array.AsReadOnly(levelPercentAdditions.ToArray());
        LevelAdditions = Array.AsReadOnly(levelAdditions.ToArray());
        Vitality = vitality;
        if (HealthLevels.Count != SacredCreatureBalanceLayout.HealthLevelPairCount ||
            HealthDifficulties.Count != SacredCreatureBalanceLayout.DifficultyCount ||
            LevelPercentAdditions.Count != SacredCreatureBalanceLayout.DifficultyCount ||
            LevelAdditions.Count != SacredCreatureBalanceLayout.DifficultyCount)
            throw new ArgumentException("Creature tuning table lengths differ from the native layout.");
        if (HealthLevels.Any(p => !float.IsFinite(p.Level) || !float.IsFinite(p.Factor)) ||
            HealthLevels.Zip(HealthLevels.Skip(1)).Any(p => p.First.Level >= p.Second.Level) ||
            HealthDifficulties.Any(f => !float.IsFinite(f)) ||
            !float.IsFinite(vitality.Base) || !float.IsFinite(vitality.Divisor) || vitality.Divisor <= 0 || !float.IsFinite(vitality.Limit))
            throw new ArgumentException("Invalid creature health tuning.");
    }
    public IReadOnlyList<SacredCreatureHealthLevel> HealthLevels { get; }
    public IReadOnlyList<float> HealthDifficulties { get; }
    public SacredSkillCurveLayout Vitality { get; }
    public IReadOnlyList<int> LevelPercentAdditions { get; }
    public IReadOnlyList<int> LevelAdditions { get; }
}
