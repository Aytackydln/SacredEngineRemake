using Sacred.Core.GameBin.Sets;
using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Core.Pak.Weapon.Details;

/// <summary>Builds structured facts from equipment and set definitions; never generates display text.</summary>
public sealed class SacredEquipmentDetailsBuilder(IReadOnlyList<SacredSetEntry> sets)
{
    public SacredEquipmentDetails Create(SacredEquipment equipment)
    {
        var bonuses = SacredEquipmentBonusDecoder.Decode(equipment);
        var identity = new SacredEquipmentIdentity(
            equipment.IdemId, equipment.Name, equipment.EquipmentType, equipment.EffectiveCharacterClassMask,
            equipment.InferredHandedness, FindSet(equipment), equipment.Price, equipment.SpawnLevel,
            SocketCount(equipment), OccupiedSocketCount(equipment));

        // Native movement penalties need hero skill data; retain only authored bonuses here.
        var movementBonus = bonuses.Where(bonus => bonus.Code == 812).Sum(bonus => bonus.Value);
        return new(identity, new(equipment.Damage, equipment.BaseStats, movementBonus), equipment.Requirements, bonuses);
    }

    private SacredEquipmentSetReference? FindSet(SacredEquipment equipment)
    {
        // The last matching entry wins, as in the original table lookup.
        var set = sets.LastOrDefault(entry => !entry.IsEmpty && entry.ItemIds.Contains(equipment.IdemId));
        if (set.ItemIds is not null)
            return new(set.SetIndex, set.NameResourceId);

        return equipment.SetType == 0 ? null : new(equipment.SetType, null);
    }

    private static int SocketCount(SacredEquipment equipment)
    {
        var count = 0;
        var slots = equipment.SlotTypes;
        foreach (var slot in slots)
        {
            if (slot != 0)
                count++;
        }

        return count;
    }

    private static int OccupiedSocketCount(SacredEquipment equipment)
    {
        var count = 0;
        var items = equipment.DefaultSlotItems;
        foreach (var item in items)
        {
            if (item != 0)
                count++;
        }

        return count;
    }
}
