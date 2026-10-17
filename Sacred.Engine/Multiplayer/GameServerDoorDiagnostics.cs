using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class GameServerDoorDiagnostics
{
    public static string Write(GoldDoorInteractionReplica doors)
    {
        foreach (var interaction in doors.Pending)
            EngineLog.WriteLine($"Network door activation pending: object {interaction.Event.ObjectReference}, " +
                $"requested {(interaction.Event.Open ? "open" : "closed")}, trigger {interaction.TriggerReference}, " +
                $"arrival state {(interaction.Trigger is { } trigger ? $"0x{trigger.State:X4}" : "unavailable")}; " +
                "native type/animation/trigger execution required.");
        return $"{doors.ReceivedCount} door receives, {doors.AppliedCount} applied, {doors.MissingOrInactiveCount} missing/inactive, " +
               $"{doors.AlreadyInStateCount} already in requested state, {doors.Pending.Count} pending native activations";
    }
}
