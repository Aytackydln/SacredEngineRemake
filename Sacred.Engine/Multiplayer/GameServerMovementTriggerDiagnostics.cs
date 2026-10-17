using System.Linq;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class GameServerMovementTriggerDiagnostics
{
    public static string Write(GoldWorldReplica world)
    {
        var triggers = world.MovementTriggers;
        var summary = $"{triggers.Sectors.Count} movement-trigger sector banks / {triggers.Sectors.Values.Sum(bank => bank.Records.Count)} records; {triggers.AppliedDeletionCount} applied deletions / {triggers.RemovedRecordCount} records removed; {world.MovementTriggerDeletions.Instructions.Count} pending routing";
        EngineLog.WriteLine($"Network triggers: {summary}; callback execution pending.");
        var variables = world.ScriptVariables;
        EngineLog.WriteLine($"Network script variables: {variables.Count} slots / {variables.Records.Count} received records; {variables.UnknownSlotCount} unknown slots; {variables.AppliedUpdateCount} incremental updates.");
        foreach (var entry in variables.Records.OrderBy(pair => pair.Key))
            EngineLog.WriteLine($"  Script variable {entry.Key}: {entry.Value.Name} = {entry.Value.Value}.");
        foreach (var entry in triggers.Sectors)
            EngineLog.WriteLine($"  Trigger sector {entry.Key}: {entry.Value.Records.Count} records.");
        return summary;
    }
}
