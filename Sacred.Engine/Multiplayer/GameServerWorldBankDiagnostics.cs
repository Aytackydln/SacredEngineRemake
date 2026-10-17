using System.Linq;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class GameServerWorldBankDiagnostics
{
    public static string WriteResources(GoldWorldReplica world)
    {
        foreach (var work in world.WorldTriggers.Pending)
            EngineLog.WriteLine($"Network trigger work pending: opcode 0x{work.Message.Opcode:X4}, slot {work.Reference}, value 0x{work.Value:X8}, name '{work.Name}'. Native gap records or inventory/timed callbacks remain required.");
        foreach (var row in world.ScriptResources.Names)
            EngineLog.WriteLine($"Network script name '{row.Name}': trigger {row.TriggerReference}, resource {row.ResourceId}; object {world.FindScriptObject(row.ResourceId)?.ToString() ?? "unresolved"}.");
        return $"{world.ScriptResources.Names.Count} native script names; {world.ScriptResources.ObjectBindings.Count} explicit resource/object bindings; {world.WorldTriggers.Records.Count} received trigger slots; {world.WorldTriggers.Pending.Count} pending trigger effects";
    }

    public static string WriteRegions(GoldWorldRegionReplica replica)
    {
        if (replica.Table is not { } table) return "native region table has not arrived";
        var marked = 0;
        for (var index = 0; index < GoldWorldRegionTableLayout.Count; index++)
        {
            var flags = table.GetFlags(index);
            if (table.IsInitialized(index)) marked++;
            if (flags != 0) EngineLog.WriteLine($"Network region {index}: flags 0x{flags:X8}; initialization flag {table.IsInitialized(index)}.");
        }
        var message = $"{GoldWorldRegionTableLayout.Count} native region records; {marked} initialization flags set; script completion is tracked separately";
        EngineLog.WriteLine(message);
        return message;
    }

    public static string Write(GoldWorldBankReplica replica)
    {
        foreach (var entry in replica.States)
        {
            var bank = entry.Value;
            EngineLog.WriteLine($"Network world bank 0x{bank.Opcode:X4}: sender {entry.Key.Sender}, origin {entry.Key.Origin}, {(bank.IsServerSnapshot ? "server snapshot" : "host upload")}, {bank.PayloadSize} bytes, {bank.RemainingData.Length} unread suffix bytes.");
            foreach (var section in bank.Sections)
            {
                EngineLog.WriteLine($"  Section {section.Index}: {section.Kind}, {section.Count} records, {section.ElementSize}-byte elements.");
                if (section.Kind is GoldWorldBankSectionKind.NestedVectors or GoldWorldBankSectionKind.RecordAndWordVectors)
                    EngineLog.WriteLine($"    {section.Records.Sum(record => record.Vectors.Sum(vector => vector.Count))} vector elements / {section.Records.Sum(record => record.ChildRecords.Count)} nested records.");
            }
            if (bank.Globals is { } globals)
                EngineLog.WriteLine($"  Global blocks: {globals.FirstBlock.Length}/{globals.SecondBlock.Length} bytes, {GoldWorldBankGlobals.TableCount} table records.");
            for (var index = bank.Sections.Count; index < GoldWorldBankLayout.HeaderWordCount; index++)
                EngineLog.WriteLine($"  Retained header word {index}: 0x{bank.GetHeaderWord(index):X8}.");
            EngineLog.WriteLine("  Script execution and host initialization pending.");
        }
        return $"{replica.States.Count} retained native world banks; host initialization pending";
    }
}
