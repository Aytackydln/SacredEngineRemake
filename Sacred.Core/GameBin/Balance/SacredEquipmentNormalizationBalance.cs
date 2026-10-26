namespace Sacred.Core.GameBin.Balance;

/// <summary>Gold bonus normalization's restriction factors from Balance.bin.</summary>
public readonly record struct SacredEquipmentNormalizationBalance(float OtherOpponentFactor,
    float HeroOpponentFactor, float HeroClassFactor);
