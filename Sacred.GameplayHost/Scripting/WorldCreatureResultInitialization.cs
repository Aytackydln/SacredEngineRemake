using Sacred.Assets.Executable;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Required first stage of nonlocal on-foot CalcResults. Equipment,
/// innate bonuses, skills, combat results and health must follow before full
/// values can be published. The existing progression writer keeps that guard.</summary>
public static class WorldCreatureResultInitialization
{
    public static uint? Apply(Span<byte> values, uint resource, uint objectReference,
        uint difficulty, bool difficultyBoostApplies, SacredCreatureTemplate? template,
        SacredCreatureResultDefinitions definitions)
        => SacredCreatureResultInitialization.Apply(values, definitions.UsesSlowDefaultSpeeds(resource),
            objectReference, difficulty, difficultyBoostApplies, template?.Endurance);
}
