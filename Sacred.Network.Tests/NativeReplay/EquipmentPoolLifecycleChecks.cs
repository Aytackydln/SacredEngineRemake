using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class EquipmentPoolLifecycleChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-equipment-pool-lifecycle.json")));
        var checks = 0;
        Check(Marshal.SizeOf<SacredScriptEquipmentPoolLayout>() == 4, "original count word layout");
        foreach (var sample in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var initial = Archive(sample.GetProperty("initial_file_hex"));
            var reload = Archive(sample.GetProperty("reload_file_hex"));
            WorldScriptExecutionContext? context = null;
            uint[][]? beforeReload = null;
            context = new(new(), [], ReadOnlyMemory<byte>.Empty, sample.GetProperty("network").GetBoolean())
            {
                EquipmentPools = initial is null ? new() : new(initial),
                EquipmentPoolReload = reload,
                TraceCommand = command =>
                {
                    if (command.Command.Opcode == SacredScriptControlOpcodes.Nop)
                        beforeReload = Snapshot(context!.EquipmentPools);
                }
            };
            Compare(context.EquipmentPools, sample.GetProperty("initial"), "native clear/reset loader baseline");
            var execute = sample.GetProperty("execute").GetBoolean();
            if (execute)
            {
                new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(sample.GetProperty("commands_hex").GetString()!));
                Check(context.StartupCompleted && !context.IsInvalidated, "valid startup and pool reload complete together");
                Check(beforeReload is not null, "observation marker executed before native-equivalent reload");
                CompareRows(beforeReload!, sample.GetProperty("after_script"), "all native intermediate appends and duplicates");
            }
            else Check(!context.StartupCompleted && beforeReload is null, "nonexecuted startup performs no reload");
            Compare(context.EquipmentPools, sample.GetProperty("final"), "all native final pool values, including file-only slot 255");
            var initialCount = Count(sample.GetProperty("initial"));
            var scriptCount = execute ? Count(sample.GetProperty("after_script")) : initialCount;
            var finalCount = Count(sample.GetProperty("final"));
            Check(context.EquipmentPools.DeclarationCount == scriptCount - initialCount, "diagnostic declaration count excludes both file loads");
            Check(context.EquipmentPools.PeakEntryCount == Math.Max(scriptCount, finalCount), "native initial/intermediate/final high-water count");
        }
        var valid = fixture.RootElement.GetProperty("cases").EnumerateArray()
            .First(sample => sample.GetProperty("initial_file_hex").ValueKind != JsonValueKind.Null);
        var sourceBytes = Convert.FromHexString(valid.GetProperty("initial_file_hex").GetString()!);
        foreach (var length in new[] { 0, 3, 4, sourceBytes.Length - 1 })
            Reject(() => SacredScriptEquipmentPoolArchive.FromPayload(sourceBytes.AsSpan(0, length)), "truncated original file");
        var bad = new byte[1024]; bad.AsSpan(0, 4).Fill(0xFF);
        Reject(() => SacredScriptEquipmentPoolArchive.FromPayload(bad), "oversized count is bounded before allocation");
        var original = SacredScriptEquipmentPoolArchive.FromPayload(sourceBytes);
        var failed = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true)
        {
            EquipmentPools = new(original),
            EquipmentPoolReload = SacredScriptEquipmentPoolArchive.FromPayload(new byte[1024])
        };
        // A failed run cannot apply the successful post-StartCode replacement.
        var firstCommand = Convert.FromHexString(valid.GetProperty("commands_hex").GetString()!).AsSpan(0, 14).ToArray();
        try { new WorldScriptExecutor(failed).ExecuteStartup(firstCommand.Concat(new byte[] { 141, 0, 4, 0 }).ToArray()); }
        catch (WorldScriptExecutionFailure)
        {
            Check(failed.IsInvalidated && !failed.StartupCompleted && failed.EquipmentPools.EntryCount == original.EntryCount + 1,
                "unresolved script failure preserves pre-reload private state for diagnosis");
            return checks;
        }
        throw new Exception("Invalid startup ran post-execution pool reload.");

        SacredScriptEquipmentPoolArchive? Archive(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null) return null;
            var bytes = Convert.FromHexString(element.GetString()!);
            var archive = SacredScriptEquipmentPoolArchive.FromPayload(bytes);
            Check(archive.ToPayload().SequenceEqual(bytes), "complete original file retained, including ignored suffix");
            var saved = bytes.ToArray(); bytes[0] ^= 0xFF;
            var output = archive.ToPayload(); output[0] ^= 0xFF;
            Check(archive.ToPayload().SequenceEqual(saved), "archive owns input/output bytes");
            Check(archive.BytesConsumed + archive.RemainingData.Length == saved.Length, "native 256-pool boundary preserves unresolved tail");
            return archive;
        }
        void Compare(WorldScriptEquipmentPools actual, JsonElement expected, string why)
        {
            CompareRows(Snapshot(actual), expected, why);
            Check(actual.EntryCount == Count(expected), why + " total");
        }
        void CompareRows(uint[][] actual, JsonElement expected, string why)
        {
            Check(actual.Length == expected.GetArrayLength(), why + " slot count");
            for (var i = 0; i < actual.Length; i++)
                Check(actual[i].SequenceEqual(expected[i].EnumerateArray().Select(value => value.GetUInt32())), why + $" slot {i}");
        }
        void Check(bool pass, string why) { if (!pass) throw new Exception("Native equipment pools: " + why); checks++; }
        void Reject(Action action, string why)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid equipment pool archive: " + why);
        }
    }
    private static int Count(JsonElement rows) => rows.EnumerateArray().Sum(row => row.GetArrayLength());
    private static uint[][] Snapshot(WorldScriptEquipmentPools pools) => Enumerable.Range(0, SacredScriptEquipmentPoolLayout.FilePoolCount)
        .Select(index => pools.Get(index).ToArray()).ToArray();
}
