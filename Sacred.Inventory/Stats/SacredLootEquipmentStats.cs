using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Loot;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Items;

namespace Sacred.Inventory.Stats;

/// <summary>Gold 0x570ED0(840, 0): find-special-items contribution, including
/// enabled sockets already decoded by SacredItemInstance. Supply equipment in
/// reference-slot order and calculated current attributes, not base attributes.</summary>
public static class SacredLootEquipmentStats
{
    public static float ForActor(SacredGameActor actor) => Calculate(actor.EquipmentSlots
        .OrderBy(slot => NativeOrder(slot.Type)).Select(slot => slot.Instance).OfType<SacredItemInstance>(), actor.TotalStats);

    // Stable ordering preserves repeated amulets/rings. SmallBelt is the same
    // native belt reference; it only differs in the presentation layout.
    private static int NativeOrder(EquipmentSlotType slot) => slot switch
    {
        EquipmentSlotType.Head => 0, EquipmentSlotType.Body => 1,
        EquipmentSlotType.Belt or EquipmentSlotType.SmallBelt => 2, EquipmentSlotType.Feet => 3,
        EquipmentSlotType.Hands => 4, EquipmentSlotType.Arms => 5, EquipmentSlotType.Amulet => 6,
        EquipmentSlotType.Ring => 8, EquipmentSlotType.LeftHand => 12, EquipmentSlotType.RightHand => 13,
        EquipmentSlotType.Cannon => 14, EquipmentSlotType.Shoulder => 15, EquipmentSlotType.Legs => 16,
        EquipmentSlotType.Wings => 17, _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    public static float Calculate(IEnumerable<SacredItemInstance> equipment, SacredActorStats attributes)
    {
        var words = new[] { SacredActorStat.Strength, SacredActorStat.Endurance, SacredActorStat.Dexterity,
            SacredActorStat.PhysicalRegeneration, SacredActorStat.MentalRegeneration, SacredActorStat.Charisma }
            .Select(stat => unchecked((ushort)(long)attributes[stat])).ToArray();
        return SacredLootBonusSum.Calculate(equipment.SelectMany(item => item.Bonuses), words);
    }
}
