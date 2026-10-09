using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Joins FX constructors to Weapon.pak visuals using the same recovered
/// predicates/modifier selectors as equipped model effects.</summary>
internal static class FxPreviewEquipment
{
    public static SacredEquipment[] Candidates(IEnumerable<SacredEquipment> equipment,
        SacredParticleDefinition definition, SacredModelFxAttachmentDefinition attachment)
    {
        var weapons = equipment.Where(item => item.Item.ModelDesc.Category == SacredItemCategory.Weapon &&
            !string.IsNullOrWhiteSpace(item.Item.ModelName)).ToArray();
        var related = weapons.Where(item => Matches(item, definition, attachment)).ToArray();
        // An event can be applied to a generated weapon whose live modifiers are
        // absent from static Weapon.pak. The preview still checks its GRN helpers.
        return related.Length > 0 ? related : weapons;
    }

    public static bool Matches(SacredEquipment item, SacredParticleDefinition definition,
        SacredModelFxAttachmentDefinition attachment) => attachment.EquipmentEffectKind switch
    {
        SacredModelEffectKind.MagicWeapon => (int)SacredEquipmentMagicEffectSelector.Select(
            item.BonusTypes, item.BonusGroups, item.EquipmentType) == (definition.Preset == 0 ? 5 : definition.Preset),
        SacredModelEffectKind.ElementalFire =>
            ElementalWeaponEffectSelector.Select(item.Damage)?.Definition.Kind == SacredElementalWeaponEffectKind.Fire,
        SacredModelEffectKind.ElementalPoison =>
            ElementalWeaponEffectSelector.Select(item.Damage)?.Definition.Kind == SacredElementalWeaponEffectKind.Poison,
        _ => SacredModelEffectCatalogue.Find(item.IdemId, item.BaseItemId)?.Kind == attachment.EquipmentEffectKind
    };
}
