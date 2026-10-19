using System;
using Sacred.Core.Pak.Weapon;
using Sacred.Particles;

namespace Sacred.Inventory.Effects;

public readonly record struct SelectedElementalWeaponEffect(
    SacredElementalWeaponEffectDefinition Definition, float Intensity);

public static class ElementalWeaponEffectSelector
{
    public static SelectedElementalWeaponEffect? Select(SacredEquipmentDamage damage)
    {
        // Gold 0x5CE08E sign-extends the four runtime maximum-damage words.
        var physical = unchecked((short)damage.PhysicalDamageMaximum);
        var fire = unchecked((short)damage.FireDamageMaximum);
        var magic = unchecked((short)damage.MagicDamageMaximum);
        var poison = unchecked((short)damage.PoisonDamageMaximum);
        var kind = fire > poison && fire > magic ? SacredElementalWeaponEffectKind.Fire
            : poison > fire && poison > magic ? SacredElementalWeaponEffectKind.Poison
            : magic > poison && magic > fire ? SacredElementalWeaponEffectKind.Magic
            : (SacredElementalWeaponEffectKind?)null;
        if (kind is null) return null;
        var elemental = kind switch
        {
            SacredElementalWeaponEffectKind.Fire => fire,
            SacredElementalWeaponEffectKind.Magic => magic,
            _ => poison
        };
        var selection = SacredElementalWeaponEffectCatalogue.Selection;
        if (elemental <= physical * selection.PhysicalThresholdScale)
            return null;
        SacredElementalWeaponEffectDefinition? definition = null;
        foreach (var candidate in SacredElementalWeaponEffectCatalogue.Definitions)
            if (candidate.Kind == kind) { definition = candidate; break; }
        if (definition is null) return null;
        var low = selection.LowDamageReference * definition.IntensityLowScale;
        var high = selection.HighDamageReference * definition.IntensityHighScale;
        var intensity = Math.Clamp((elemental - low) / (high - low), 0, 1);
        return intensity > selection.MinimumIntensity ? new SelectedElementalWeaponEffect(definition, intensity) : null;
    }
}
