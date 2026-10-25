using System;
using System.Linq;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class NetworkConsumerCheats
{
    public static string Execute(string value, GoldWorldReplica world)
    {
        var delivery = world.Consumers; var diagnostics = delivery.Diagnostics;
        diagnostics.Log = EngineLog.WriteLine;
        if (value.StartsWith("mark:", StringComparison.OrdinalIgnoreCase))
        { diagnostics.SetMark(value[5..]); return $"Consumer observation marked {diagnostics.Mark}."; }
        if (value.StartsWith("trace:", StringComparison.OrdinalIgnoreCase))
        {
            var trace = value[6..].ToLowerInvariant();
            if (trace is not ("off" or "all" or "texts" or "hud" or "combat" or "fx" or "actions" or "chat"))
                return "Use set server-consumers trace:<off|all|texts|hud|combat|fx|actions|chat>.";
            diagnostics.Trace = trace; return $"Consumer trace: {trace}.";
        }
        if (!value.Equals("status", StringComparison.OrdinalIgnoreCase))
            return "Use set server-consumers <status|mark:label|trace:off|trace:all|trace:texts|trace:hud|trace:combat|trace:fx|trace:actions|trace:chat>.";
        foreach (var report in delivery.Reports.TakeLast(12))
            EngineLog.WriteLine($"Consumer {report.Consumer}: session {report.Work.Session}, order {report.Work.Order}, 0x{report.Work.Opcode:X3}, sender {report.Work.Sender}, origin {report.Work.Origin}; {report.Outcome}: {report.Detail}.");
        foreach (var fault in diagnostics.Faults)
            EngineLog.WriteLine($"Consumer delivery fault: {fault.Key}; {fault.Value} lost entries; reconnect or rebuild required.");
        return $"Consumer session {delivery.Session}, {delivery.Published} work entries, capacity {delivery.Capacity}, {delivery.Evicted} evicted; " +
            $"mark {diagnostics.Mark}: {diagnostics.Received} receipts, trace {diagnostics.Trace}; " +
            string.Join(", ", diagnostics.Opcodes.OrderBy(pair => pair.Key).Select(pair => $"0x{pair.Key:X3}={pair.Value}"));
    }
}
