namespace Sacred.Core.GameBin.Balance;

/// <summary>Original Balance.bin tuning shared by equipment construction stages.</summary>
public sealed record SacredEquipmentGenerationTuning(SacredEquipmentGenerationBalance Levels,
    SacredEquipmentBonusBalance Bonuses, SacredEquipmentNormalizationBalance Normalization,
    SacredEquipmentSocketBalance Sockets, SacredEquipmentPricingBalance Pricing, SacredEquipmentMagicBalance? Magic = null,
    SacredLootBalance? Loot = null);
