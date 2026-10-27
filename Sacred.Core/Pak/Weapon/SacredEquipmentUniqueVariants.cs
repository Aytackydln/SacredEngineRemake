using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x53445A..0x5344FF: filter an already selected modifier
/// family's current shared records, then draw one eligible unique resource.</summary>
public static class SacredEquipmentUniqueVariants
{
    public static uint? Select(IReadOnlyList<uint> family, ushort heroLevel, uint excludedMinimum, uint excludedMaximum,
        Func<uint, SacredEquipmentSelectionCandidate> candidate, SacredRandomSequence random)
    {
        var eligible = new List<uint>();
        foreach (var resource in family)
        {
            var item = candidate(resource);
            if (resource >= excludedMinimum && resource <= excludedMaximum) continue;
            if ((item.Values.Flags & 0xF0000) != 0xF0000 || item.SetType != 0 || item.Values.MinimumLevel > heroLevel) continue;
            eligible.Add(resource);
        }
        return eligible.Count == 0 ? null : eligible[SacredRandomRange.Inclusive(0, eligible.Count - 1, random)];
    }
}
