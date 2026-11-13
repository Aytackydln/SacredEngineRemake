using Sacred.Assets.Equipment;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>EquipNPC's 0x556F10/0x556A30 on a dormant unloaded actor and
/// unplaced installed Weapon. The caller discards the detached hand; it does
/// not restore it. Loaded, placed and default-item lifetimes remain separate.</summary>
internal static class WorldScriptEquipmentHandRefresh
{
    public static void Validate(WorldCreatureInitialActor actor, WorldEquipmentInitialItem held,
        ushort slot, SacredEquipmentGenerationData data, WorldScriptHandRefreshContext? context)
    {
        var item = held.Physical;
        if (context is null || context.Template.ItemType != actor.Base.ObjectType || actor.Node.Active ||
            actor.KiMode != 0 || actor.EiMode != 0 ||
            (item.Base.Flags & (2 | 0x1000 | GoldPhysicalStateLayout.ModelLoadedFlag)) != 2 ||
            item.AttachmentParent != actor.Base.ObjectReference || (item.PhysicalFlags & 8) != 0 ||
            data.EquipmentDefaults!.Resource(actor.Base.ObjectType, slot) != 0)
            throw new NotSupportedException("EquipNPC hand refresh requires its dormant unloaded unplaced Weapon and no default lifetime.");
    }

    public static WorldCreatureInitialActor Detach(WorldCreatureInitialActor actor, ushort slot,
        WorldEquipmentInitialItem held, SacredEquipmentGenerationData data,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> registry, WorldScriptHandRefreshContext context,
        uint difficulty, byte owner, ICollection<GoldMessage> publications)
    {
        Validate(actor, held, slot, data, context);
        // Native sends even during startup, after the incoming item stream.
        publications.Add(GoldEquipmentRemovalCodec.Create(owner, owner, actor.Base.ObjectReference, slot, 0));
        var item = held.Physical;
        // Flag-2 cItem placement leaves its coordinates/cache unchanged. With
        // no loaded model and no 0x1000 flag, binding-kind model detach is inert.
        item.SetLink(0, false);
        item.Base = item.Base with { Word34 = 0, Flags = item.Base.Flags & ~0x1000u };
        var references = actor.EquipmentReferences.ToArray();
        references[slot] = 0;
        actor = actor with { EquipmentReferences = references };
        actor = WorldCreatureEquippedResults.Apply(actor, context.Template, context.Results, data,
            registry, difficulty, context.LocalReference, true, context.Players);
        // CalcResults(0,1) marks valid. CreateNPC's later networking override
        // belongs to that caller and is not performed by this detach caller.
        return actor with { Component = actor.Component with { ResultValid = 1 } };
    }
}
