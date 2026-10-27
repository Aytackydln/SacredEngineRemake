using Sacred.Core.Pak.Items;

namespace Sacred.Core.Loot;

public enum SacredCreatureDeathRewardKind { Equipment, Bonus, ExtraCalls, UniqueWorldResource, SetResource, Jackpot, ResetBoost }
public readonly record struct SacredEquipmentRewardRequest(uint Chance, uint MinimumQuality, uint MaximumQuality, int LevelIncrement);
public sealed record SacredCreatureDeathRewardRule(SacredCreatureDeathRewardKind Kind, int BonusIndex = -1,
    int MinimumExtraCalls = 0, int MaximumExtraCalls = 0, SacredEquipmentRewardRequest Equipment = default,
    uint Resource = 0, int RandomMaximum = 0);

/// <summary>Original resource dispatch and operand tables extracted from Gold.</summary>
public sealed record SacredCreatureDeathRewardRules(IReadOnlyDictionary<uint, SacredCreatureDeathRewardRule> Resources,
    int FirstHeroSet, int LastHeroSet, IReadOnlyList<uint> HeroSetResources,
    IReadOnlyList<SacredItemCategory> JackpotCategories, uint UniqueExcludedMinimum, uint UniqueExcludedMaximum);

public readonly record struct SacredCreatureUniqueRewardQuery(SacredItemCategory Category, ushort SourceLevel, uint HeroPreference);
