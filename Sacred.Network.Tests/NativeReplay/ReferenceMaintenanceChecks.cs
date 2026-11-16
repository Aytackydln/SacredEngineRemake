using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ReferenceMaintenanceChecks
{
    private sealed class ObjectState(uint address)
    {
        public uint Address { get; } = address;
        public uint Reference { get; set; } = 17;
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-reference-maintenance.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var slots = Enumerable.Repeat<ObjectState?>(null, 32).ToList();
            if (row.GetProperty("occupied").GetBoolean()) slots[17] = new(data.GetProperty("actor").GetUInt32());
            var available = UInts(row, "pending").ToList();
            var state = new WorldObjectReferenceMaintenanceState(Records(row, "records").ToList(), row.GetProperty("last").GetUInt32());
            var cleanup = new List<uint>();
            var diagnostics = new List<uint>();
            var clocks = 0;
            WorldObjectReferenceMaintenance.Maintain(row.GetProperty("cancel").GetUInt32(), row.GetProperty("multiplayer").GetBoolean(),
                slots, available, state, new WorldObjectReferenceMaintenanceCallbacks<ObjectState>(() =>
                {
                    clocks++;
                    return row.GetProperty("now").GetUInt32();
                }, _ => 1234, reference =>
                {
                    cleanup.Add(reference);
                    WorldObjectReferenceCleanup.Clean(reference, false, slots, available,
                        new WorldObjectReferenceCleanupCallbacks<ObjectState>(value => value.Reference,
                            (value, next) => value.Reference = next, _ => 0x10, null,
                            diagnostic => diagnostics.Add(Format(diagnostic.Kind))));
                }, (_, _) => diagnostics.Add(0x95ce44), _ => diagnostics.Add(0x95cc6c)));
            Check(clocks == 1, "clock sampled once after cancellation");
            Check(state.LastSweep == row.GetProperty("last_after").GetUInt32(), "strict unsigned sweep interval and empty-table update");
            Check(state.Records.SequenceEqual(Records(row, "records_after")), "first-match cancellation and expired-row swap order");
            Check(available.SequenceEqual(UInts(row, "pending_after")), "return queue after actual cleanup composition");
            Check(cleanup.SequenceEqual(UInts(row, "cleanup")), "cleanup calls including zero, suppressed for occupied slots");
            Check(diagnostics.SequenceEqual(UInts(row, "diagnostics")), "ordered occupied/return/cleanup diagnostics");
            Check((slots[17]?.Address ?? 0) == row.GetProperty("slot17").GetUInt32(), "no repair/destruction of occupied expired slot");
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Reference maintenance: " + why);
            checks++;
        }
    }
    private static IEnumerable<uint> UInts(JsonElement row, string name) => row.GetProperty(name).EnumerateArray().Select(value => value.GetUInt32());
    private static IEnumerable<WorldObjectReferenceDelay> Records(JsonElement row, string name) => row.GetProperty(name).EnumerateArray()
        .Select(value => new WorldObjectReferenceDelay(value[0].GetUInt32(), value[1].GetUInt32()));
    private static uint Format(WorldObjectReferenceCleanupDiagnosticKind kind) => kind switch
    {
        WorldObjectReferenceCleanupDiagnosticKind.ZeroReference => 0x95cd88,
        WorldObjectReferenceCleanupDiagnosticKind.Repair => 0x95cc44,
        WorldObjectReferenceCleanupDiagnosticKind.LiveReference => 0x95cd48,
        WorldObjectReferenceCleanupDiagnosticKind.Duplicates => 0x95cd00,
        WorldObjectReferenceCleanupDiagnosticKind.DuplicateIndex => 0x95ccf8,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
