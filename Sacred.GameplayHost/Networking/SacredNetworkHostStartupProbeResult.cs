using Sacred.GameplayHost.Scripting;

namespace Sacred.GameplayHost.Networking;

public sealed record SacredNetworkHostStartupProbeResult(byte PlayerSlot, int GrantedObjects, int RemainingObjects,
    WorldScriptExecutionContext Execution, WorldScriptTriggerRegistry Triggers, WorldScriptObjectWorld Objects)
{
    /// <summary>Resolved file inputs for the incomplete creature command.
    /// See CreaturePreparation for any private initialized stages.</summary>
    public WorldScriptCreatureBlueprint? PendingCreature { get; init; }
    /// <summary>Private stages reached before the incomplete opcode failed;
    /// reservations expire when the diagnostic disconnects. Never upload-ready.</summary>
    public WorldScriptCreaturePreparation? CreaturePreparation { get; init; }
}
