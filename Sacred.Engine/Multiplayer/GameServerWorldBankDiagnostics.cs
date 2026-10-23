using System.Linq;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class GameServerWorldBankDiagnostics
{
    public static string WriteMovementCallbacks(GoldMovementCallbackReplica callbacks)
    {
        foreach (var group in callbacks.Groups)
            EngineLog.WriteLine($"Network movement callbacks '{group.Name}': {string.Join(", ", group.Functions.Select(value => $"{(value.IsHash ? "hash" : "index")}:0x{value.Value:X8}"))}.");
        return $"{callbacks.Groups.Count} received callback groups; {callbacks.PendingChangeCount} script-context changes pending; crossing execution remains separate";
    }
    public static string WriteDefinitions(GoldScriptDefinitionReplica definitions)
    {
        for (var index = 0; index < definitions.Records.Count; index++)
        {
            var row = definitions.Records[index];
            EngineLog.WriteLine($"Network definition slot {index}: kind {row.Kind}, '{row.Name}'; parameters {string.Join(", ", Enumerable.Range(0, 8).Select(row.Parameter))}.");
        }
        return $"{definitions.Records.Count} received/baseline definitions; initialized {definitions.IsInitialized}; {definitions.PendingCount} awaiting baseline; {definitions.PendingChangeCount} script-context changes pending";
    }
    public static string WriteFunctions(GoldScriptFunctionReplica functions)
    {
        foreach (var pair in functions.Updates)
        {
            var row = pair.Value;
            EngineLog.WriteLine($"Network function slot {pair.Key}: '{row.Name}', code {row.Start}+{row.ByteLength}, quest {row.Quest}, number {row.Number}, deleted 0x{row.Deleted:X8} (flag {row.DeletedFlag}).");
        }
        return functions.IsConfigured
            ? $"{functions.Definitions.Count} campaign function slots; {functions.Updates.Count} received replacements"
            : $"function table waiting for campaign data; {functions.PendingCount} pending slot updates";
    }
    public static string WriteScriptTables(GoldScriptTableReplica tables)
    {
        foreach (var pair in tables.Tables)
            EngineLog.WriteLine($"Network script table 0x{pair.Key:X4}: {pair.Value.Count} records of {pair.Value.RecordSize} bytes, {pair.Value.RemainingData.Length} partial-record bytes; field meanings unresolved.");
        return $"{tables.Tables.Count}/3 bulk script tables received";
    }
    public static string WriteScriptRegions(GoldScriptRegionReplica regions)
    {
        foreach (var group in regions.Regions)
            EngineLog.WriteLine($"Network script region key {group.Key}: {group.Value.Count} rectangle records.");
        return $"{regions.Regions.Count} script-region keys / {regions.Regions.Values.Sum(rows => rows.Count)} records; audio and gameplay consumers pending";
    }
    public static string WriteTriggerPatches(GoldTriggerPatchReplica patches)
    {
        foreach (var position in patches.Positions.Take(20))
            EngineLog.WriteLine($"Network trigger-patch position: sector {position.Sector}, tile {position.X},{position.Y}/{position.Surface}.");
        return $"{patches.MappingEntries.Count} trigger mappings / {patches.Mappings.Count} distinct keys; {patches.Positions.Count} positions; {patches.PendingRoutingCount} awaiting KEYX; {patches.PendingTileCount} queued live tile marks; callback execution pending";
    }
    public static string WriteRegistrations(GoldScriptRegistrationReplica registrations)
    {
        foreach (var patch in registrations.Mouseovers.Active)
            EngineLog.WriteLine($"Network mouseover: tile {patch.X},{patch.Y}/{patch.Surface}; priority {patch.InsertAtFront}.");
        return $"{registrations.SpawnRenewals.Count} spawn-renew records (scheduler pending); {registrations.Mouseovers.Active.Count} active / {registrations.PendingMouseovers.Count} pending mouseovers; {registrations.PendingTileCount} queued tile marks";
    }
    public static string WriteActualQuests(GoldActualQuestsReplica quests)
    {
        for (var slot = 0; slot < GoldActualQuestsLayout.SlotCount; slot++)
            EngineLog.WriteLine($"Network actual-quest slot {slot}: quest word {Word(quests.GetQuestWord(slot))}; companion word {Word(quests.GetCompanionWord(slot))}.");
        return $"{quests.KnownByteCount}/{GoldActualQuestsLayout.Size} actual-quest table bytes received";
    }
    private static string Word(uint? value) => value is { } word ? $"0x{word:X8}" : "unknown";

    public static string WriteSpatial(GoldScriptSpatialReplica spatial)
    {
        foreach (var icon in spatial.MapIcons)
            EngineLog.WriteLine($"Network map record: sector {icon.Sector}, position {icon.X},{icon.Y}/{icon.Surface}; kind {icon.Kind}; label {icon.TextResource}, curvature {icon.Curvature}, angle {icon.AngleDegrees}.");
        return $"{spatial.MapIcons.Count} received map icons/labels; {spatial.Attractors.Count} received attractors; attractor AI execution pending";
    }
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
            var state = table.GetState(index);
            if (flags != 0 || state.Status != 0 || state.RegionFactor != 1 || state.StoryFactor != 1 || state.DynamicFactor != 1)
                EngineLog.WriteLine($"Network region {index}: flags 0x{flags:X8}; initialization flag {table.IsInitialized(index)}; status {state.Status}/{state.RequiredStatus}; factors {state.RegionFactor}/{state.StoryFactor}/{state.DynamicFactor}; merchant factor {state.MerchantFactor}.");
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
