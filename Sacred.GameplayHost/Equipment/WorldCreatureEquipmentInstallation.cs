namespace Sacred.GameplayHost.Equipment;

/// <summary>Gold 0x555E00 / 0x556A30 state changes and ordered calls. Selection
/// is separate; even an invalid normal slot runs incoming item normalization.</summary>
public sealed class WorldCreatureEquipmentInstallation(IWorldCreatureEquipmentEffects effects, bool networkActive)
{
    public uint Install(WorldCreatureEquipmentState actor, ushort slot, uint reference, bool publish)
    {
        if (slot == 18)
        {
            actor[slot] = reference;
            if (reference != 0)
            {
                effects.SetMount(reference);
                actor.Base = actor.Base with { Flags = actor.Base.Flags | 0x80 };
            }
            Publish(0x11E, actor, slot, reference, publish);
            return 0;
        }
        if (actor.Horse && slot != 0) return 0;
        var incoming = effects.Find(reference);
        if (incoming is { Physical: true, Item: true }) effects.Normalize(incoming, actor.Base.ObjectType);
        if (slot >= 19) return 0;
        var previous = Detach(actor, slot, false);
        if (incoming is { Physical: true }) incoming.Base = incoming.Base with { Flags = incoming.Base.Flags & ~0x100000u };
        if (reference != 0)
        {
            if (effects.Exists(actor[slot]))
            {
                effects.Delete(actor[slot]);
                previous = 0;
            }
            actor[slot] = reference;
            if (incoming is { Physical: true })
            {
                incoming.SetLink(actor.Base.ObjectReference, true);
                effects.PrepareAttachment(incoming);
            }
            slot = effects.AttachModel(actor, slot);
            Publish(0x11E, actor, slot, reference, publish);
            if (incoming is { Physical: true })
            {
                if ((incoming.Base.Flags & 2) == 0) effects.RetireFromWorld(incoming);
                incoming.Base = incoming.Base with { Sector = 0, X = 0, Y = 0, Surface = 0 };
            }
        }
        effects.EquipmentChanged(actor, slot);
        if ((actor.Behavior & 0x104) != 0)
        {
            if (incoming is { Physical: true }) incoming.PhysicalFlags |= 0x10;
            if (effects.Find(previous) is { Physical: true } old && (old.PhysicalFlags & 0x10) == 0)
            {
                // The original dereferences the incoming physical object here.
                if (incoming is not { Physical: true })
                    throw new InvalidOperationException("Native retained-equipment replacement requires an incoming physical object.");
                incoming.PhysicalFlags |= 8;
                actor.Retain(previous);
                previous = 0;
            }
        }
        return previous;
    }

    public uint Detach(WorldCreatureEquipmentState actor, ushort slot, bool publish)
    {
        if (slot >= 19) return 0;
        var previous = actor[slot];
        if (slot == 18)
        {
            actor[slot] = 0;
            if (networkActive && publish && effects.PublishMountRemoval(actor, effects.Find(previous)))
                effects.Publish(0x11D, actor.Base.ObjectReference, slot, 0);
            actor.Base = actor.Base with { Flags = actor.Base.Flags & ~0x80u };
            previous = 0;
        }
        else if (effects.Exists(previous) && effects.DefaultResource(actor.Base.ObjectType, slot) ==
                 (effects.Find(previous) ?? throw new InvalidOperationException("Existing native object lookup failed.")).Base.ObjectType)
        {
            previous = 0;
        }
        else if (previous != 0)
        {
            effects.EnsureActorModel(actor);
            var old = effects.Find(previous);
            Publish(0x11D, actor, slot, 0, publish);
            if (old is { Item: true }) effects.ResetItemPlacement(old, actor);
            if (old is { Physical: true }) effects.DetachModel(old, slot);
            if (old is not null)
            {
                old.SetLink(0, false);
                old.Base = old.Base with { Word34 = 0, Flags = old.Base.Flags & ~0x1000u };
            }
            if (old is { Physical: true } && (old.PhysicalFlags & 8) != 0) previous = 0;
            actor[slot] = 0;
            effects.Recalculate(actor);
        }
        var fallback = effects.DefaultResource(actor.Base.ObjectType, slot);
        if (fallback != 0) actor[slot] = effects.CreateDefault(fallback);
        return previous;
    }

    private void Publish(ushort opcode, WorldCreatureEquipmentState actor, ushort slot, uint item, bool publish)
    {
        if (networkActive && publish) effects.Publish(opcode, actor.Base.ObjectReference, slot, item);
    }
}
