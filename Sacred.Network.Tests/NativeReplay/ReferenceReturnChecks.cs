using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ReferenceReturnChecks
{
    private sealed class ObjectState { public uint Reference { get; set; } = 17; }
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-reference-return.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var slots = Enumerable.Repeat<ObjectState?>(null, 32).ToList();
            if (row.GetProperty("occupied").GetBoolean()) slots[17] = new();
            var available = UInts(row, "pending").ToList();
            var cleanup = new List<uint>();
            var diagnostics = new List<uint>();
            WorldObjectReferenceReturn.Return(row.GetProperty("reference").GetUInt32(), row.GetProperty("reason").GetUInt32(),
                row.GetProperty("multiplayer").GetBoolean(), available, (_, _) => diagnostics.Add(0x95cc6c), reference =>
                {
                    cleanup.Add(reference);
                    WorldObjectReferenceCleanup.Clean(reference, false, slots, available,
                        new WorldObjectReferenceCleanupCallbacks<ObjectState>(value => value.Reference, (value, next) => value.Reference = next,
                            _ => 0x10, null, diagnostic => diagnostics.Add(diagnostic.Kind switch
                            {
                                WorldObjectReferenceCleanupDiagnosticKind.ZeroReference => 0x95cd88,
                                WorldObjectReferenceCleanupDiagnosticKind.Repair => 0x95cc44,
                                WorldObjectReferenceCleanupDiagnosticKind.LiveReference => 0x95cd48,
                                WorldObjectReferenceCleanupDiagnosticKind.Duplicates => 0x95cd00,
                                WorldObjectReferenceCleanupDiagnosticKind.DuplicateIndex => 0x95ccf8,
                                _ => throw new ArgumentOutOfRangeException(nameof(diagnostic))
                            })));
                });
            Check(available.SequenceEqual(UInts(row, "pending_after")), "original returned queue with actual cleanup");
            Check(cleanup.SequenceEqual(UInts(row, "cleanup")), "reason-three gate including zero reference");
            Check(diagnostics.SequenceEqual(UInts(row, "diagnostics")), "ordered reason-range and cleanup diagnostics");
            Check((slots[17] is null ? 0 : native.RootElement.GetProperty("actor").GetUInt32()) == row.GetProperty("slot17").GetUInt32(), "return does not destroy marked object");
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Reference return: " + why);
            checks++;
        }
    }
    private static IEnumerable<uint> UInts(JsonElement row, string name) => row.GetProperty(name).EnumerateArray().Select(value => value.GetUInt32());
}
