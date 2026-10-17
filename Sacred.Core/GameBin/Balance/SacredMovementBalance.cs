using System.Collections.Frozen;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Original movement tuning. Horse profile keys are runtime HorseClass selectors,
/// with zero selecting one. On-foot heroes use the ordinary CalcResults baseline.</summary>
public sealed class SacredMovementBalance(float movementScale,
    IReadOnlyDictionary<uint, SacredMovementProfileLayout> profiles, SacredSkillCurveLayout armorLoreMovement)
{
    public float MovementScale { get; } = movementScale;
    public SacredSkillCurveLayout ArmorLoreMovement { get; } = armorLoreMovement;
    public FrozenDictionary<uint, SacredMovementProfileLayout> Profiles { get; } = profiles.ToFrozenDictionary();
    // Native std::map::operator[] value-initializes a missing profile.
    public SacredMovementProfileLayout GetProfile(byte horseClass) => Profiles.GetValueOrDefault(horseClass == 0 ? 1u : horseClass);
}
