namespace Sacred.Core.GameBin.Balance;

/// <summary>FillRegion timing in Balance.bin's counted global block.</summary>
public static class SacredPopulationBalanceLayout
{
    /// <summary>Float seconds used when last timestamp / 1000 and weather-kind-4 predicate are both zero.</summary>
    public const int FirstThresholdOffset = 0x7A0;
    /// <summary>Float seconds used otherwise. These thresholds compare to absolute performance-clock seconds.</summary>
    public const int SubsequentThresholdOffset = 0x9CC;
}
