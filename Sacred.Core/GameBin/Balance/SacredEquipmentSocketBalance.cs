using System.Collections.ObjectModel;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Original socket roll thresholds and costs, plus Balance.bin's third
/// counted map (native 0xAD57C8), shared by rarity cost and generation quality.</summary>
public sealed class SacredEquipmentSocketBalance
{
    public SacredEquipmentSocketBalance(uint firstChance, uint additionalChance, IEnumerable<float> costs,
        IReadOnlyDictionary<uint, uint> qualityBudgets, IReadOnlyDictionary<uint, int>? maximumSockets = null)
    {
        Costs = Array.AsReadOnly(costs.ToArray());
        if (Costs.Count != 3 || Costs.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Socket tuning requires three finite cost factors.");
        FirstChance = firstChance;
        AdditionalChance = additionalChance;
        QualityBudgets = new ReadOnlyDictionary<uint, uint>(qualityBudgets.ToDictionary());
        MaximumSockets = new ReadOnlyDictionary<uint, int>((maximumSockets ?? new Dictionary<uint, int>()).ToDictionary());
    }
    public uint FirstChance { get; }
    public uint AdditionalChance { get; }
    public IReadOnlyList<float> Costs { get; }
    public IReadOnlyDictionary<uint, uint> QualityBudgets { get; }
    /// <summary>First-vector-map defaults keyed by wpmod WeaponType. Runtime TypeManager
    /// overrides, if supplied by scripts, take precedence outside this file table.</summary>
    public IReadOnlyDictionary<uint, int> MaximumSockets { get; }
    public int SocketMaximum(uint weaponType) => MaximumSockets.GetValueOrDefault(weaponType);
    /// <summary>Native map[] inserts zero for a missing key. No guessed budget.</summary>
    public uint Budget(uint quality) => QualityBudgets.GetValueOrDefault(quality);
}
