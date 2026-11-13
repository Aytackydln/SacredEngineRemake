using Sacred.Assets.Equipment;
using Sacred.Assets.Executable;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold EquipItem's target-empty Armor branch for assigned unloaded NPCs.
/// The native item stream precedes installation. Unplaced unloaded Weapon hand refresh is supported; default/occupied target
/// lifetimes and loaded models remain separate.</summary>
public static class WorldScriptArmorEquipmentCommand
{
    public static (WorldCreatureInitialActor Actor, WorldEquipmentInitialItem Item, GoldMessage Publication, IReadOnlyList<GoldMessage> Publications) Apply(
        WorldCreatureInitialActor actor, ItemsPakEntry descriptor, WorldEquipmentInitialFactory factory,
        SacredEquipmentGenerationData data, SacredEquipmentSlotDefinitions slots, SacredCreatureBalance progression,
        SacredRandomSequence random, uint difficulty, byte owner, double clock, WorldScriptHandRefreshContext? refresh = null)
    {
        if (actor.RelationClass == (uint)Sacred.Core.Pak.Creature.SacredCreatureClass.Horse || actor.Base.ObjectReference <= 16 || actor.KiMode != 0 || actor.EiMode != 0 ||
            actor.Base.Owner != owner || actor.Node.Active || (actor.RuntimeFlags & 0x2000) != 0 ||
            (actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 ||
            descriptor.ModelDesc.Category is not (SacredItemCategory.Helmet or SacredItemCategory.ChestArmor or
                SacredItemCategory.ShoulderArmor or SacredItemCategory.FootArmor or SacredItemCategory.ArmArmor or SacredItemCategory.Wings or SacredItemCategory.Belt))
            throw new NotSupportedException("EquipItem Armor requires its named dormant assigned unloaded KI-zero NPC.");
        var selected = WorldCreatureEquipmentSelection.Resolve(descriptor.ModelDesc.Category, null, false,
            actor.Base.ObjectReference, actor.EquipmentReferences);
        if (actor.EquipmentReferences[(int)selected] != 0 || data.EquipmentDefaults is null ||
            data.EquipmentDefaults.Resource(actor.Base.ObjectType, (ushort)selected) != 0)
            throw new NotSupportedException("EquipItem Armor requires its target-empty slot without a default-item lifetime.");
        // Preflight the native left/right queries before allocation. Shield is
        // not a Weapon; actual detach follows publication, right then left.
        var detach = new List<ushort>();
        foreach (ushort hand in new ushort[] { 12, 13 })
        {
            var reference = actor.EquipmentReferences[hand];
            if (reference == 0) continue;
            if (!factory.Items.TryGetValue(reference, out var held))
                throw new InvalidDataException("EquipItem requires its current hand object graph.");
            var type = held.Physical.Base.ObjectType;
            var category = data.Templates.GetItem(type).ModelDesc.Category;
            if (category == SacredItemCategory.Shield) continue;
            if (category != SacredItemCategory.Weapon || !data.Templates.TryGetSharedTemplate(type, out var shared) ||
                data.HandRefresh is null)
                throw new NotSupportedException("EquipItem requires original Weapon hand-refresh definitions and its shared profile.");
            if (data.HandRefresh.RequiresRefresh(type, shared.UsageIdentifier, shared.BaseItemId))
            {
                WorldScriptEquipmentHandRefresh.Validate(actor, held, hand, data, refresh);
                detach.Add(hand);
            }
        }
        var item = factory.Create(descriptor, data.Templates.GetInstance(descriptor.ItemIndex), clock, networking: true);
        WorldScriptEquipmentCommandGeneration.Apply(item, System.Runtime.InteropServices.MemoryMarshal.Read<SacredActorValuesLayout>(actor.Component.Values).Level, difficulty, progression, data, random);
        var state = GoldItemState.Create(GoldPhysicalConstructionWriter.Write(item.Physical.Base, item.Physical.Direction),
            [], item.Values, item.InventoryId, []);
        item.SerializedState = state;
        item.Physical.Base = item.Physical.Base with { Flags = item.Physical.Base.Flags & ~0x10000u };
        var publication = GoldObjectStateCodec.Create(0x16e, owner, state.ToPayload());
        var publications = new List<GoldMessage> { publication };
        // Native tests right first, then the saved left reference.
        foreach (var hand in detach.AsEnumerable().Reverse())
            actor = WorldScriptEquipmentHandRefresh.Detach(actor, hand, factory.Items[actor.EquipmentReferences[hand]],
                data, factory.Items, refresh!, difficulty, owner, publications);
        actor = WorldCreatureInitialEquipmentInstallation.Apply(actor, item, descriptor.ItemIndex, data, slots,
            factory.Items, networking: true);
        return (actor, item, publication, publications.AsReadOnly());
    }
}
