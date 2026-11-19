using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>C2 saved objects on the existing factory/scheduler using the plan's
/// consumed real reservations. The complete compared file-read batch validates
/// before any object/counter mutation. Does not attach gear or grant readiness.</summary>
public static class WorldEquipmentSavedCharacterImport
{
    public static void Restore(GoldSavedCharacterReferencePlan plan, SacredEquipmentGenerationData data,
        GoldObjectReferencePool references, WorldEquipmentInitialFactory factory, double schedulerClock)
    {
        if (factory.Items.Count != 0) throw new InvalidOperationException("Saved item factory is already populated.");
        var pending = new List<(ItemsPakEntry Descriptor, byte[] Initial, uint Reference, WorldEquipmentSavedState State)>();
        foreach (var record in plan.Source.Objects.Records)
        {
            if (record.IsEmpty) continue;
            if (record.Item is not { } item || record.Resource != item.BaseState.Resource)
                throw new NotSupportedException("Saved character object list requires its compared item-only factory path.");
            var reference = plan.References[item.BaseState.ObjectReference];
            if (!references.IsAllocated(reference) || factory.Items.ContainsKey(reference))
                throw new InvalidOperationException("Saved item plan does not belong to consumed actual grants.");
            var descriptor = data.Templates.GetItem(record.Resource);
            var initial = data.Templates.GetInstance(record.Resource);
            if (descriptor.ModelDesc.Category is not (SacredItemCategory.Book or SacredItemCategory.Potion or SacredItemCategory.Rune))
                _ = Sacred.GameplayHost.Runtime.SacredStartupHeading.ToDirection(descriptor.ModelDesc.Angle3D);
            var state = WorldEquipmentSavedImport.Prepare(item, descriptor, data.PriceContext(item.ItemResource), data.Tuning);
            pending.Add((descriptor, initial, reference, state));
        }
        if (pending.Count != plan.ObjectCount || !double.IsFinite(schedulerClock) || schedulerClock < 0)
            throw new InvalidDataException("Saved item factory batch differs from its complete character plan.");
        foreach (var row in pending)
        {
            factory.RestoreAllocated(row.Descriptor, row.Initial, row.State, schedulerClock, row.Reference);
        }
    }
}
