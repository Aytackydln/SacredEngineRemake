using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Native 0x54ACC0 / 0x54B130 slot choice, before installation effects.
/// Player-slot model availability is a required caller service; assigned NPCs use
/// the native reference > 16 bypass. Selecting a slot does not install an item.</summary>
public static class WorldCreatureEquipmentSelection
{
    public static SacredCreatureEquipmentSlot Resolve(SacredItemCategory category, byte? usage, bool forcesLeftHand,
        uint actorReference, ReadOnlySpan<uint> equipment, Func<int, bool>? modelSlotAvailable = null)
    {
        if (equipment.Length != 19) throw new ArgumentException("Expected nineteen native equipment references.", nameof(equipment));
        if (forcesLeftHand || category == SacredItemCategory.Shield) return SacredCreatureEquipmentSlot.LeftHand;
        if (category == SacredItemCategory.ChestArmor) return SacredCreatureEquipmentSlot.Body;
        if (category == SacredItemCategory.Helmet) return SacredCreatureEquipmentSlot.Head;
        if (category == SacredItemCategory.Weapon)
            return usage is 7 or 12 ? SacredCreatureEquipmentSlot.LeftHand : SacredCreatureEquipmentSlot.RightHand;
        if (category is SacredItemCategory.Ring or SacredItemCategory.Amulet)
        {
            var first = category == SacredItemCategory.Ring ? 8 : 6;
            var end = category == SacredItemCategory.Ring ? 12 : 8;
            for (var slot = first; slot < end; slot++)
                if (Available(slot) && equipment[slot] == 0) return (SacredCreatureEquipmentSlot)slot;
            return SacredCreatureEquipmentSlot.Unavailable;
        }
        var selected = category switch
        {
            SacredItemCategory.Wings => SacredCreatureEquipmentSlot.Wings,
            SacredItemCategory.FootArmor => SacredCreatureEquipmentSlot.Feet,
            SacredItemCategory.Gloves => SacredCreatureEquipmentSlot.Gloves,
            SacredItemCategory.ArmArmor => SacredCreatureEquipmentSlot.Arms,
            SacredItemCategory.ShoulderArmor => SacredCreatureEquipmentSlot.Shoulder,
            SacredItemCategory.Belt => SacredCreatureEquipmentSlot.Belt,
            SacredItemCategory.LegArmor => SacredCreatureEquipmentSlot.Legs,
            SacredItemCategory.HorseEquipment => SacredCreatureEquipmentSlot.Body,
            SacredItemCategory.DwarfCannon => SacredCreatureEquipmentSlot.Cannon,
            _ => SacredCreatureEquipmentSlot.Unavailable
        };
        if (category is SacredItemCategory.HorseEquipment or SacredItemCategory.DwarfCannon) return selected;
        return selected == SacredCreatureEquipmentSlot.Unavailable || !Available((int)selected) ? SacredCreatureEquipmentSlot.Unavailable : selected;

        bool Available(int slot) => actorReference > 16 || (modelSlotAvailable ?? throw new InvalidOperationException("Player-slot equipment selection requires original model slot availability."))(slot);
    }

    public static SacredCreatureEquipmentSlot FindInstallationSlot(SacredItemCategory category, byte? usage, bool forcesLeftHand,
        uint itemReference, uint actorReference, ReadOnlySpan<uint> equipment, Func<int, bool>? modelSlotAvailable = null)
    {
        var selected = Resolve(category, usage, forcesLeftHand, actorReference, equipment, modelSlotAvailable);
        if (itemReference == 0 || selected == SacredCreatureEquipmentSlot.Unavailable) return SacredCreatureEquipmentSlot.Unavailable;
        if (equipment[(int)selected] == 0) return selected;
        if (selected == SacredCreatureEquipmentSlot.RightHand && equipment[12] == 0 && category == SacredItemCategory.Weapon)
            return SacredCreatureEquipmentSlot.LeftHand;
        return SacredCreatureEquipmentSlot.Unavailable;
    }
}
