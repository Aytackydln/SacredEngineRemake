using Sacred.GameplayHost.Scripting;

namespace Sacred.GameplayHost.Networking;

/// <summary>Generated state belongs to its live initialization session. Completed
/// bytecode does not establish callbacks, complete banks, upload or actor readiness.</summary>
public sealed record WorldHostStartupState(WorldScriptExecutionContext Execution,
    WorldScriptTriggerRegistry Triggers, WorldScriptObjectWorld Objects,
    WorldScriptCreatureBlueprint? PendingCreature);
