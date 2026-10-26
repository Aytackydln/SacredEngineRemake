using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x570ED0(840, 0) / 0x568C40: ordered magnitudes from resolved
/// enabled equipment/socket records. Attributes are current ST, WI, GS, RP, RM, CH.</summary>
public static class SacredLootBonusSum
{
    public static float Calculate(IEnumerable<SacredEquipmentBonus> bonuses, IReadOnlyList<ushort> attributes)
    {
        if (attributes.Count != 6) throw new ArgumentException("Six current attribute words are required.", nameof(attributes));
        float total = 0;
        foreach (var bonus in bonuses)
        {
            if (bonus.Code != 840) continue;
            var index = bonus.AttributeSelector switch { 1 => 0, 2 => 2, 3 => 1, 4 => 3, 5 => 4, 6 => 5, _ => -1 };
            var value = index >= 0 ? attributes[index] * (double)bonus.Value * .01f : bonus.Value;
            total = (float)(total + value);
        }
        return total;
    }
}
