using Sacred.GameplayHost.Scripting;

namespace Sacred.GameplayHost.Networking;

public sealed record SacredNetworkHostStartupProbeResult(byte PlayerSlot, int GrantedObjects, int RemainingObjects,
    WorldScriptExecutionContext Execution, WorldScriptTriggerRegistry Triggers, WorldScriptObjectWorld Objects)
{
    /// <summary>Resolved file inputs only. The probe has not allocated or constructed this creature.</summary>
    public WorldScriptCreatureBlueprint? PendingCreature { get; init; }
}
