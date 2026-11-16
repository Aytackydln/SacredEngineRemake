using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Ordinary unloaded NPC identity/heading in Gold 0x5FB66C..0x5FB77D.
/// Allocation/counter assignment and node activation precede this stage. The
/// original event 0x40 updates physical direction, not cached base +0x28.
/// Placement, sector lifetime, scripts, equipment and publication follow.</summary>
public static class WorldCreatureFactoryIdentity
{
    public static WorldCreatureInitialActor Assign(WorldCreatureInitialActor actor, ItemsPakEntry descriptor,
        uint reference, byte owner, byte allocationCounter, bool owned)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.Node.Active || actor.Base.ObjectReference != 0 || actor.Base.Owner != 0 ||
            (actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 || actor.EquipmentReferences.Any(value => value != 0))
            throw new NotSupportedException("Initial identity requires the activated, unassigned, unloaded empty-equipment NPC.");
        var localHero = actor.RelationClass == (uint)SacredCreatureClass.Hero && reference == owner && reference is >= 1 and <= 16;
        if (reference <= 16 && !localHero || owner is < 1 or > 16 || descriptor.ItemIndex != actor.Base.ObjectType ||
            descriptor.ModelDesc.Category != SacredItemCategory.Creature)
            throw new ArgumentException("Expected a reserved NPC reference, valid owner and matching original creature descriptor.");
        var direction = SacredStartupHeading.ToDirection(descriptor.ModelDesc.Angle3D);
        var flags = actor.Base.Flags | 0x400000;
        if (owned) flags |= 0x10000000;
        if (descriptor.ModelDesc.RequiresGraphicObjectRegistration) flags |= 0x800;
        return actor with
        {
            Base = actor.Base with { ObjectReference = reference, Owner = owner, Byte38 = allocationCounter, Flags = flags },
            Direction = new(direction.X, direction.Y, direction.Z)
        };
    }
}
