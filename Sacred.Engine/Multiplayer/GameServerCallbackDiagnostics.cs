using Sacred.Network.Connection;

namespace Sacred.Engine.Multiplayer;

internal static class GameServerCallbackDiagnostics
{
    public static string Write(GoldGameConnection? connection)
    {
        if (connection is null) return "No network connection callback history.";
        var diagnostics = connection.Diagnostics;
        foreach (var row in diagnostics.Recent)
            EngineLog.WriteLine($"Network protocol {(row.Outgoing ? "send" : "receive")}: {row.Milliseconds} ms; " +
                $"0x{row.Opcode:X3}, sequence {row.Sequence}, sender {row.Sender}, origin {row.Origin}, {row.PayloadBytes} bytes.");
        return Summary(connection);
    }

    public static string Summary(GoldGameConnection connection)
    {
        var diagnostics = connection.Diagnostics;
        return $"{diagnostics.ReceivedMessages} received, {diagnostics.SentMessages} sent; " +
            $"time callbacks {diagnostics.TimeRepliesSent}/{diagnostics.TimeProbesReceived} replied; " +
            $"{diagnostics.GameplayPulsesSent} gameplay pulses sent; " +
            $"close reason: {connection.CloseReason ?? "connection active"}";
    }
}
