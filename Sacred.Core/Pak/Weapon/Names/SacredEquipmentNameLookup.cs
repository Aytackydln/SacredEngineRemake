using System.Globalization;
using Sacred.Core.GameRes;

namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>Original numeric resource or bit-31-tagged string hash lookup.</summary>
public static class SacredEquipmentNameLookup
{
    public static string Resolve(GameResStore resources, uint reference)
    {
        if (reference is 0 or uint.MaxValue) return "";
        var hash = (reference & 0x80000000) != 0 ? reference & 0x7FFFFFFF
            : SacredResourceHash.Compute(reference.ToString(CultureInfo.InvariantCulture));
        return resources.Strings.GetValueOrDefault(hash, "");
    }
}
