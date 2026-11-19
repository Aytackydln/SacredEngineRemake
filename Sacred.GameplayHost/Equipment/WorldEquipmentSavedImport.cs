using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Reached Gold 5C7690/5CF1A0 file read with no animation or embedded
/// records. Calculation happens privately before factory/ID mutation. C2 final
/// equipment normalization, attachments and world registration remain separate.</summary>
public static class WorldEquipmentSavedImport
{
    public static WorldEquipmentSavedState Prepare(SacredSavedItem source, ItemsPakEntry descriptor,
        SacredEquipmentPriceContext priceContext, SacredEquipmentGenerationTuning tuning)
    {
        var saved = source.BaseState;
        if (source.Count != 0 || saved.Resource != descriptor.ItemIndex ||
            !WorldEquipmentInitialFactory.Supports(descriptor.ModelDesc.Category) ||
            descriptor.ModelDesc.Category is SacredItemCategory.Book or SacredItemCategory.QuestItem or SacredItemCategory.HorseEquipment ||
            source.HasWeaponExtension != (descriptor.ModelDesc.Category == SacredItemCategory.Weapon) ||
            priceContext.Category != descriptor.ModelDesc.Category ||
            BinaryPrimitives.ReadUInt32LittleEndian(source.PhysicalData[SacredSavedPhysicalLayout.AnimationSelectionOffset..]) != 0)
            throw new NotSupportedException("Saved item requires its compared ordinary matching resource, no embedded records and no model activation.");
        var flags = (saved.Flags & ~0x04009000u);
        if ((flags & 2) == 0) flags |= 0x400000;
        var physicalFlags = BinaryPrimitives.ReadUInt32LittleEndian(source.PhysicalData[SacredSavedPhysicalLayout.WordB0Offset..]) | 4;
        if ((flags & 0x800000) != 0) { physicalFlags &= ~4u; flags &= ~0x200u; }
        var basis = new GoldObjectBaseState(saved.ObjectReference, saved.Resource, flags, saved.Sector,
            saved.X, saved.Y, saved.Surface, saved.Word28, saved.Word34, saved.Byte38, saved.Owner, saved.Word3C);
        var values = source.ItemData.ToArray();
        // Original 428110 Potion path returns the saved price without mutation.
        if (descriptor.ModelDesc.Category != SacredItemCategory.Potion)
        {
            var price = SacredEquipmentPricing.CalculateOrdinary(values, priceContext, tuning.Pricing, tuning.Levels,
                tuning.Bonuses, tuning.Normalization, tuning.Sockets, SacredX87Precision.Single);
            BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(8), price);
        }
        return new(source, basis, physicalFlags, values);
    }

    internal static void Commit(WorldEquipmentInitialItem item, WorldEquipmentSavedState saved, byte owner)
    {
        if (item.SavedState is not null || item.Generated || !item.Node.Active || item.Physical.Base.ObjectType != saved.FileBase.ObjectType)
            throw new InvalidOperationException("Saved item commit requires its fresh active factory object.");
        var reference = item.Physical.Base.ObjectReference;
        var flags = saved.FileBase.Flags & ~0x200010u;
        if ((flags & 2) != 0) flags &= ~0x30u;
        item.Physical.Base = saved.FileBase with { ObjectReference = reference, Owner = owner, Flags = flags };
        item.Physical.SetLink(saved.Source.BaseState.Word2C, (flags & 0x400) != 0);
        item.Physical.PhysicalFlags = saved.PhysicalFlags;
        item.Physical.Direction = Vector(saved.Source.PhysicalData, SacredSavedPhysicalLayout.Vector70Offset);
        item.NextOnTile = 0;
        item.Values = saved.CopyValues();
        item.SavedState = saved;
    }

    private static GoldPhysicalVector Vector(ReadOnlySpan<byte> bytes, int at) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 8)..]));
}
