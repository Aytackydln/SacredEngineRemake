using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ReferenceCleanupChecks
{
    private sealed class ObjectState(uint reference, uint flags)
    {
        public uint Reference { get; set; } = reference;
        public uint Flags { get; } = flags;
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-reference-cleanup.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var kind = row.GetProperty("kind").GetString();
            var wrong = row.GetProperty("wrong").GetBoolean();
            var objects = new[] { new ObjectState(wrong ? 1u : 17, kind == "marked" ? 0x10u : 0),
                new ObjectState(wrong ? 1u : 18, kind is "marked" or "mixed" ? 0x10u : 0) };
            var slots = Enumerable.Repeat<ObjectState?>(null, row.GetProperty("count").GetInt32()).ToList();
            if (kind != "hole")
                for (var i = 0; i < objects.Length; i++) if (17 + i < slots.Count) slots[17 + i] = objects[i];
            var available = row.GetProperty("pending").EnumerateArray().Select(value => value.GetUInt32()).ToList();
            var diagnostics = new List<uint[]>();
            // The oracle zero-initializes its complete supplied deque storage,
            // then sets the predecessor word explicitly.
            var outside = new Dictionary<int, uint> { [-1] = row.GetProperty("predecessor").GetUInt32() };
            var corrupt = row.GetProperty("pending_after").ValueKind == JsonValueKind.Null;
            var guarded = false;
            try
            {
                WorldObjectReferenceCleanup.Clean(row.GetProperty("requested").GetUInt32(), row.GetProperty("gate").GetBoolean(),
                    slots, available, new WorldObjectReferenceCleanupCallbacks<ObjectState>(value => value.Reference,
                        (value, reference) => value.Reference = reference, value => value.Flags,
                        () => outside[-1], value => diagnostics.Add(ToNative(value)))
                    {
                        ReadOutsideLogical = index => outside.GetValueOrDefault(index),
                        WriteOutsideLogical = (index, value) => outside[index] = value
                    });
            }
            catch (NotSupportedException) when (corrupt) { guarded = true; }
            catch (NotSupportedException error) { throw new Exception($"Unexpected cleanup guard: {row}", error); }
            Check(guarded == corrupt, "native negative-extent controls are explicitly guarded");
            if (corrupt) continue;
            Check(available.SequenceEqual(row.GetProperty("pending_after").EnumerateArray().Select(value => value.GetUInt32())), $"native swap-removal: managed [{string.Join(',', available)}], native {row}");
            Check(objects.Select(value => value.Reference).SequenceEqual(row.GetProperty("references").EnumerateArray().Select(value => value.GetUInt32())), "stored-reference repair including marked objects");
            Check(outside[-1] == row.GetProperty("predecessor_after").GetUInt32(), "native write before logical begin");
            var expected = row.GetProperty("events").EnumerateArray().ToArray();
            Check(diagnostics.Count == expected.Length, "native diagnostic count and gate suppression");
            for (var i = 0; i < diagnostics.Count; i++)
                Check(diagnostics[i].SequenceEqual(expected[i].EnumerateArray().Select(value => value.GetUInt32())), "ordered native diagnostic arguments including negative-index bits");
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Reference cleanup: " + why);
            checks++;
        }
    }

    private static uint[] ToNative(WorldObjectReferenceCleanupDiagnostic value) => value.Kind switch
    {
        WorldObjectReferenceCleanupDiagnosticKind.ZeroReference => [0x95cd88, value.First],
        WorldObjectReferenceCleanupDiagnosticKind.Repair => [0x95cc44, value.First, value.Second],
        WorldObjectReferenceCleanupDiagnosticKind.LiveReference => [0x95cd48, value.First, value.Second],
        WorldObjectReferenceCleanupDiagnosticKind.Duplicates => [0x95cd00, value.First, value.Second],
        WorldObjectReferenceCleanupDiagnosticKind.DuplicateIndex => [0x95ccf8, value.First],
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
