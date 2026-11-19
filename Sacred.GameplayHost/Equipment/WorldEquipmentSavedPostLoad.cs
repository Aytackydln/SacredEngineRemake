using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Gold C2 605FDF..606062, before equipment/reference binding.
/// The native main-record getter orders all damage bounds, even when the
/// category does not enter the price branch. No generated-item or upload
/// readiness follows from completing this saved-item stage.</summary>
public static class WorldEquipmentSavedPostLoad
{
    /// <summary>The original loader can complete one item immediately after its reader.</summary>
    public static void Complete(WorldEquipmentInitialItem item, SacredEquipmentPriceContext context,
        SacredEquipmentGenerationTuning tuning)
    {
        if (item.SavedPostLoadCompleted) return;
        item.Values = Prepare(item, context, tuning);
        item.SavedPostLoadCompleted = true;
    }

    public static byte[] Prepare(WorldEquipmentInitialItem item, SacredEquipmentPriceContext context,
        SacredEquipmentGenerationTuning tuning)
    {
        if (item.SavedState is not { } saved || saved.Source.Count != 0 || !item.Node.Active ||
            item.Generated || item.PacketReady || item.Values.Length != SacredEquipmentInstanceLayout.Size ||
            BinaryPrimitives.ReadUInt32LittleEndian(item.Values) != saved.Source.ItemResource)
            throw new NotSupportedException("C2 normalization requires its actual current saved item, with no embedded records or generated/public state.");
        var values = item.Values.ToArray();
        SacredEquipmentDamageBounds.Normalize(values);
        // This is the native category dispatcher, not a resource-ID list.
        if (RecalculatesPrice(context.Category))
        {
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(4));
            BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(4), flags & 0x7fffffffu);
            var price = SacredEquipmentPricing.CalculateOrdinary(values, context, tuning.Pricing, tuning.Levels,
                tuning.Bonuses, tuning.Normalization, tuning.Sockets, SacredX87Precision.Single);
            BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(8), price);
        }
        return values;
    }

    public static void Complete(WorldEquipmentInitialFactory factory, SacredEquipmentGenerationData data)
    {
        var pending = new List<(WorldEquipmentInitialItem Item, byte[] Values)>();
        foreach (var item in factory.Items.Values)
        {
            if (item.SavedPostLoadCompleted) continue;
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(item.Values);
            pending.Add((item, Prepare(item, data.PriceContext(resource), data.Tuning)));
        }
        // Validate the entire batch before changing any current object values.
        foreach (var row in pending)
        {
            row.Item.Values = row.Values;
            row.Item.SavedPostLoadCompleted = true;
        }
    }

    private static bool RecalculatesPrice(SacredItemCategory category) => category is
        SacredItemCategory.Weapon or SacredItemCategory.ChestArmor or SacredItemCategory.Ring or SacredItemCategory.Shield or
        SacredItemCategory.Helmet or SacredItemCategory.FootArmor or SacredItemCategory.Belt or SacredItemCategory.Amulet or
        SacredItemCategory.ShoulderArmor or SacredItemCategory.ArmArmor or SacredItemCategory.LegArmor or SacredItemCategory.Gloves or
        SacredItemCategory.Wings or SacredItemCategory.Projectile or SacredItemCategory.HorseEquipment;
}
