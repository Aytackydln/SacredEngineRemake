using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold dialog-only SetNPCState registration and actor mutation.
/// Missing names and number zero clear the binding. The explicit dialog setter
/// publishes even during startup; result recomputation belongs to the caller.</summary>
public static class WorldCreatureScriptDialogState
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor,
        WorldScriptRegistrationTable table, SacredScriptDialogSelection selection)
    {
        var selected = WorldCreatureScriptDialog.Resolve(table, selection);
        table.AttachState(actor.Base.ObjectReference, actor.DialogIndex, selected);
        return actor with
        {
            DialogIndex = selected > 0 ? (uint)selected : 0,
            Base = actor.Base with { Flags = selected > 0 ? actor.Base.Flags | 0x80000 : actor.Base.Flags & ~0x80000u }
        };
    }
}
