namespace Sacred.Core.GameBin.Balance;

/// <summary>Original Balance.bin inputs to Gold's rune threshold (0x5598B0).</summary>
public sealed record SacredRuneLootBalance(float ChanceFactor, float InventoryDivisor,
    float BoostedFactor, float RatioLimit, IReadOnlyList<float> ClassLevelFactors);
