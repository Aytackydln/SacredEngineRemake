using Sacred.Assets.Equipment;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Native CreateNPC's initial 0x54B130/0x555E00 branch. Reuses the
/// general installer with concrete compared unloaded effects. Target occupancy,
/// defaults, loaded models and live publication need their own caller bodies.</summary>
public static class WorldCreatureInitialEquipmentInstallation
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, WorldEquipmentInitialItem item,
        uint authoredResource, SacredEquipmentGenerationData data, SacredEquipmentSlotDefinitions slots,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> registry, bool networking)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(registry);
        if (actor.Base.ObjectReference <= 16 || actor.KiMode != 0 || actor.EiMode != 0 ||
            (actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 ||
            !item.InitialValuesPrepared || (item.Physical.Base.Flags & (2 | GoldPhysicalStateLayout.ModelLoadedFlag)) != 2 ||
            item.Physical.AttachmentParent != 0 || !registry.TryGetValue(item.Physical.Base.ObjectReference, out var current) || !ReferenceEquals(current, item))
            throw new NotSupportedException("Initial installation requires an assigned KI-zero unloaded NPC and its fresh generated registered item.");
        if (data.EquipmentSources is null || data.EquipmentDefaults is null)
            throw new InvalidDataException("Initial installation requires original equipment aliases and default resources.");
        var descriptor = data.Templates.GetItem(authoredResource);
        var usage = data.Templates.TryGetSharedTemplate(authoredResource, out var shared) ? shared.UsageIdentifier : (byte?)null;
        var selected = WorldCreatureEquipmentSelection.FindInstallationSlot(descriptor.ModelDesc.Category, usage,
            slots.ForcesLeftHand(authoredResource), item.Physical.Base.ObjectReference, actor.Base.ObjectReference, actor.EquipmentReferences);
        if (selected == SacredCreatureEquipmentSlot.Unavailable || actor.EquipmentReferences[(int)selected] != 0 ||
            data.EquipmentDefaults.Resource(actor.Base.ObjectType, (ushort)selected) != 0)
            throw new NotSupportedException("Initial installation requires an available empty slot without a default-item lifetime.");
        // The script calls the own-value getter after generation and before selection.
        SacredEquipmentDamageBounds.Normalize(item.Values);
        var state = new WorldCreatureEquipmentState(actor.Base, false, actor.EquipmentReferences) { Behavior = actor.BehaviorFlags };
        var effects = new WorldCreatureInitialEquipmentEffects(data, registry, actor);
        var previous = new WorldCreatureEquipmentInstallation(effects, networking).Install(state, (ushort)selected,
            item.Physical.Base.ObjectReference, publish: false);
        if (previous != 0) throw new InvalidOperationException("Initial target-empty installation returned an old item.");
        return actor with { Base = state.Base, EquipmentReferences = state.References.ToArray() };
    }
}
