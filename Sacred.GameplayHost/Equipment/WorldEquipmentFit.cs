using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;

namespace Sacred.GameplayHost.Equipment;

public readonly record struct WorldEquipmentFitContext(uint HeroResource, bool HeroClass, SacredEquipmentFitHero Values);

/// <summary>Actual class and requirement fit for a resolved concrete item.
/// Returned flags must be written back even when requirements reject it. The
/// caller owns object/type/value lookup and temporary object lifetime.</summary>
public sealed class WorldEquipmentFit(SacredEquipmentGenerationData data)
{
    public SacredEquipmentClassFitResult Evaluate(uint resource, uint flags, SacredEquipmentInstanceLayout? values,
        WorldEquipmentFitContext hero, Func<byte, byte> skillRank, bool classOnly = false, bool hasItem = true,
        Func<SacredEquipmentVariantResult>? resolvedVariant = null)
    {
        if (!hasItem) return new(false, flags);
        var rules = data.EquipmentClassFitRules ?? throw new InvalidOperationException("Original equipment fit rules unavailable.");
        var preferences = data.SelectionPreferences ?? throw new InvalidOperationException("Original class preference rules unavailable.");
        var usage = data.Templates.TryGetSharedTemplate(resource, out var shared) ? shared.UsageIdentifier : (byte?)null;
        var fit = SacredEquipmentClassFit.Evaluate(resource, hero.HeroResource, hero.HeroClass,
            data.Templates.GetItem(resource).ModelDesc.Category, usage, flags, rules, preferences, Variant);
        return fit with { Allowed = SacredEquipmentFit.Requirements(fit.Allowed, classOnly, values, hero.Values, skillRank) };

        SacredEquipmentVariantResult Variant()
        {
            if (resolvedVariant is not null) return resolvedVariant();
            return new WorldEquipmentVariants(data).Resolve(resource, hero.HeroResource);
        }
    }
}
