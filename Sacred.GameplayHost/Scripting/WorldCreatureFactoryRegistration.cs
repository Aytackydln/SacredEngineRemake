using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FB77D..0x5FBA1F unpublished ordinary NPC continuation.
/// Unplaced sector registration does nothing. Descriptor graphic flag 0x0100
/// appends to the manager's graphic vector. Multiplayer with a newly allocated
/// reference sets pending bit 0x10000; the base writer later clears it on wire.</summary>
public sealed class WorldCreatureFactoryRegistration
{
    private readonly List<uint> _graphicObjects = [];
    public IReadOnlyList<uint> GraphicObjects => _graphicObjects.AsReadOnly();

    public WorldCreatureInitialActor CompleteUnpublished(WorldCreatureInitialActor actor,
        ItemsPakEntry descriptor, bool networking, bool newReference)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.Node.Active || actor.Base.ObjectReference <= 16 || (actor.Base.Flags & 2) == 0 ||
            actor.RelationClass == (uint)SacredCreatureClass.Hero || descriptor.ItemIndex != actor.Base.ObjectType ||
            descriptor.ModelDesc.Category != SacredItemCategory.Creature)
            throw new NotSupportedException("Factory continuation requires an assigned, unplaced ordinary NPC.");
        if (descriptor.ModelDesc.RequiresGraphicObjectRegistration)
            _graphicObjects.Add(actor.Base.ObjectReference);
        return actor with { Base = actor.Base with { Flags = actor.Base.Flags | (networking && newReference ? 0x10000u : 0) } };
    }
}
