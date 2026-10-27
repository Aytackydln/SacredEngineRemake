using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

public sealed record SacredEquipmentClassFitRules(IReadOnlySet<uint> ForcedUnusableResources, uint MinimumVariantAlias);
public readonly record struct SacredEquipmentClassFitResult(bool Allowed, uint Flags);

/// <summary>Gold cItemBase virtual +0x38 (0x5BF810). May rewrite concrete
/// class flags after a successful visual variant lookup, even before a later
/// attribute requirement fails. Variant lookup and Hero classification are
/// explicit services; resource exceptions come from original instructions.</summary>
public static class SacredEquipmentClassFit
{
    public static SacredEquipmentClassFitResult Evaluate(uint resource, uint heroResource, bool heroClass,
        SacredItemCategory category, byte? usage, uint flags, SacredEquipmentClassFitRules rules,
        SacredEquipmentPreferenceRules preferences, Func<SacredEquipmentVariantResult> variant)
    {
        if ((flags & 0x40) != 0 && heroClass) return new(false, flags);
        if (category == SacredItemCategory.Weapon && usage.HasValue)
        {
            if (heroResource == 8 && usage is 7 or 12 or 4 || heroResource == 9 && usage == 7 ||
                usage == 3 && heroResource is not (2 or 8)) return new(false, flags);
        }
        if (category == SacredItemCategory.HorseEquipment && heroResource == 8) return new(false, flags);
        const uint classes = 0xC0003F;
        if (rules.ForcedUnusableResources.Contains(resource)) flags = (flags & ~classes) | 0x40;
        if ((flags & 0x40) != 0 && unchecked((int)heroResource) < 32) return new(false, flags);
        var restriction = flags & classes;
        if (restriction is 0 or classes) return new(true, flags);
        var result = variant();
        if (!result.Supported) return new(false, flags);
        var mask = ClassMask(heroResource, preferences);
        if (result.ReplacementType != 0)
        {
            flags &= ~classes;
            if (heroResource <= 9 || heroResource >= rules.MinimumVariantAlias) flags |= mask;
            return new(true, flags);
        }
        return new(mask != 0 ? (flags & mask) != 0 : unchecked((int)heroResource) <= 9, flags);
    }

    private static uint ClassMask(uint hero, SacredEquipmentPreferenceRules preferences) => hero switch
    {
        >= 1 and <= 6 => 1u << ((int)hero - 1),
        7 => 0x20,
        8 => 0x400000,
        9 => 0x800000,
        _ => preferences.ResourceMasks.GetValueOrDefault(hero)
    };
}
