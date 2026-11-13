using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class RegionReplacementBankChecks
{
    public static byte[] BankOneWireExample(string root)
    {
        // Other blocks/sections are the existing native reader control, not host production.
        var nativeEnvelope = RemainingWorldBankChecks.BankOneWireExample(root);
        var context = Context(Definitions(root).CreateRegionReplacements());
        new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
        context.WriteRegionReplacementBankBlock().CopyTo(nativeEnvelope,
            GoldWorldBankLayout.HeaderSize + GoldWorldBankGlobals.FirstBlockSize);
        return nativeEnvelope;
    }

    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-region-replacement-bank.json")));
        var checks = 0;
        var definitions = Definitions(root);
        foreach (var sample in fixture.RootElement.GetProperty("reset_cases").EnumerateArray())
        {
            var context = Context(definitions.CreateRegionReplacements());
            Reject(() => context.WriteRegionReplacementBankBlock(), "incomplete startup");
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            Compare(context.WriteRegionReplacementBankBlock(), Bytes(sample, "after_hex"), "original reset survives complete wea.bin loader");
            Compare(context.WriteRegionReplacementBankBlock(), Bytes(sample, "bank_hex"), "original bank fixed-block writer");
            Compare(context.WriteRegionReplacementBankBlock(), Bytes(sample, "save_hex"), "original interpreter save writer");
            Check(sample.GetProperty("bytes_consumed").GetInt32() == 4648 &&
                sample.GetProperty("pool_sizes").EnumerateArray().Sum(value => value.GetInt32()) == 906,
                "native loader consumes original file and separate 256 pool vectors");
        }
        foreach (var sample in fixture.RootElement.GetProperty("opaque_cases").EnumerateArray())
        {
            var original = Bytes(sample, "block_hex");
            var context = Context(original);
            original[0] ^= 0xFF;
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            var output = context.WriteRegionReplacementBankBlock();
            Compare(output, Bytes(sample, "bank_hex"), "restored opaque headers/unused entry bytes match native bank write");
            Compare(output, Bytes(sample, "save_hex"), "restored original save bytes");
            output[0] ^= 0xFF;
            Compare(context.WriteRegionReplacementBankBlock(), Bytes(sample, "block_hex"), "input and output arrays are owned");
            var first = MemoryMarshal.Read<SacredScriptRegionReplacementLayout>(Bytes(sample, "block_hex"));
            Check(first.ReplacementBehaviorMask == BinaryPrimitives.ReadUInt32LittleEndian(Bytes(sample, "block_hex")), "known behavior-mask field retains full UInt32 bits");
        }
        foreach (var sample in fixture.RootElement.GetProperty("mutation_cases").EnumerateArray())
        {
            // Reuse the receipt reducer: script and receive both call original 4ABFE0.
            var world = new GoldWorldReplica();
            var state = world.CreatureCommands.Interactions.RegionTypes;
            var initial = Bytes(sample, "initial_hex");
            for (var i = 0; i < 50; i++) state.Seed(i, initial.AsSpan(i * 172, 172));
            state.ConfigureGeneration(sample.GetProperty("current_generation").GetUInt32());
            var entry = GoldCreatureInteractionCodec.Decode(new GoldMessage(GoldCreatureInteractionCodec.RegionTypesOpcode,
                0, 1, 0, Bytes(sample, "request_hex")));
            world.Apply(entry);
            var after = new byte[8600];
            foreach (var pair in state.Records) pair.Value.Span.CopyTo(after.AsSpan(pair.Key * 172, 172));
            Compare(after, Bytes(sample, "after_hex"), "existing receipt reducer matches original script parser/helper mutations");
            Check(state.Pending.Count == 1 && state.UnsafeAppendCount == 0, "world consequences remain ordered pending work");
            var context = Context(after);
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            Compare(context.WriteRegionReplacementBankBlock(), Bytes(sample, "bank_hex"), "nonzero tables preserve actual native writer bytes");

            var unsupported = Context(initial);
            try { new WorldScriptExecutor(unsupported).ExecuteStartup(Bytes(sample, "command_hex")); }
            catch (WorldScriptExecutionFailure)
            {
                Check(unsupported.IsInvalidated, "unported script target transformations invalidate the private run");
                Reject(() => unsupported.WriteRegionReplacementBankBlock(), "failed mutation cannot serialize baseline as final");
                continue;
            }
            throw new Exception("Unimplemented opcode-116 target transformations were accepted.");
        }
        var unknown = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
        foreach (var sample in fixture.RootElement.GetProperty("region_tail_cases").EnumerateArray())
        {
            var table = GoldWorldRegionTable.CreateInitial().WithFlags(sample.GetProperty("region").GetUInt32(),
                sample.GetProperty("flags").GetUInt32(), true);
            Compare(table.ToPayload(), Bytes(sample, "after_hex"), "adjacent region-table constructor and native flag setter");
            Compare(table.ToPayload(), Bytes(sample, "bank_hex"), "bank tail writes all 256 region records verbatim");
        }
        new WorldScriptExecutor(unknown).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
        Reject(() => unknown.WriteRegionReplacementBankBlock(), "missing reset/restored provenance");
        var custom = Context(definitions.CreateRegionReplacements());
        var mutation = Convert.FromHexString("74000400");
        new WorldScriptExecutor(custom, (_, _) => true).ExecuteStartup(mutation);
        Reject(() => custom.WriteRegionReplacementBankBlock(), "custom handler cannot credit an unimplemented table mutation");
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Native replacement bank: " + why); checks++; }
        void Compare(byte[] actual, byte[] expected, string why) => Check(actual.AsSpan().SequenceEqual(expected), why);
        void Reject(Action action, string why)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid replacement bank: " + why);
        }
    }

    private static WorldScriptExecutionContext Context(byte[] baseline) => new(new(), [], ReadOnlyMemory<byte>.Empty, true)
    { RegionReplacements = SacredScriptRegionReplacementTable.Read(baseline) };

    private static SacredScriptInitializationDefinitions Definitions(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-initial-globals.json")));
        var json = fixture.RootElement;
        return SacredScriptInitializationDefinitions.Read(Bytes(json, "parameter_code_hex")
            .Concat(new byte[16]).Concat(Bytes(json, "clear_code_hex")).ToArray());
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
