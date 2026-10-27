namespace Sacred.GameplayHost.Equipment;

/// <summary>Resolved inputs after death eligibility, killer attribution and
/// source-specific reward/count dispatch. Those preceding callbacks are not
/// inferred from a creature ID or silently replaced by this decision service.
/// This boundary covers ordinary, unforced invocations; authored/progression
/// forced resources must follow their separate native entry paths.</summary>
public sealed record WorldCreatureLootDecisionContext(
    uint SourceReference, uint SourceResource, uint SourceCategory, ushort SourceLevel, ushort ExperienceMaximum,
    uint KillerReference, ushort HeroLevel, uint HeroResource, ushort EffectiveFind, uint SurvivalSeconds,
    ushort ChanceCurveSkillValue, uint Difficulty, uint NearbyPartyMembers, bool BoostedSource,
    uint SourceRollCount, uint SourceBonus, uint LearnedCombatArtRankSum, uint InventoryRuneCount);
