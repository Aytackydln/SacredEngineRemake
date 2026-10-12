using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Inventory.Actors;

/// <summary>Resolves empty-slot visuals from Items.pak's fallback flag and Weapon.pak's
/// authored hero flags. Day/night forms retain distinct raw bits; Underworld heroes
/// use the upper class flags instead of the night form's low-byte bit.</summary>
public static class SacredActorAppearance
{
    public static void Configure(SacredGameActor actor, IEnumerable<SacredEquipment> definitions)
    {
        var template = actor.Progression.Template;
        var candidates = template is { Class: not SacredCreatureClass.Hero }
            ? [] : definitions.Where(item => item.IsAppearanceFallback &&
                Matches(actor.CharacterClass, template?.ItemType, item.Classification)).ToArray();
        foreach (var slot in actor.EquipmentSlots)
            slot.SetDefaultAppearance(candidates.Where(item => EquipmentSlotRules.AcceptsItem(slot.Type, item))
                .Select(item => (SacredEquipment?)item).SingleOrDefault());
    }

    private static bool Matches(SacredCharacterClass characterClass, uint? heroType,
        SacredEquipmentClassification classification)
    {
        if (characterClass == SacredCharacterClass.Dwarf) return (classification.ClassFlagCode & 0x40) != 0;
        if (characterClass == SacredCharacterClass.Daemon) return (classification.ClassFlagCode & 0x80) != 0;
        var mask = characterClass == SacredCharacterClass.Vampiress && heroType == 7
            ? 1 << 6 : (byte)characterClass.ToMask();
        return (classification.CharacterClassMaskCode & mask) != 0;
    }
}
