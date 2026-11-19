using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostSectorEntryChecks
{
    public static int Run(string root)
    {
        using var selector = HostSectorInitializationChecks.Read(root, "native-host-sector-enter-selector");
        using var body = HostSectorInitializationChecks.Read(root, "native-host-sector-enter-body");
        var proof = body.RootElement;
        var source = MemoryMarshal.Read<KeyxSectorRecord>(Bytes(proof, "keyx_hex"));
        var checks = 0;
        foreach (var row in selector.RootElement.GetProperty("cases").EnumerateArray())
        {
            var functions = row.GetProperty("names").EnumerateArray().Select(value => new SacredScriptFunction(value.GetString()!, 0, 0, -1, 0, 0)).ToArray();
            var context = new WorldScriptExecutionContext(new(), functions, ReadOnlyMemory<byte>.Empty, true);
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            var sector = new WorldScriptSectorEnvironment(source);
            uint? cached = row.GetProperty("cached").ValueKind == JsonValueKind.Null ? null : row.GetProperty("cached").GetUInt32();
            var index = WorldScriptSectorEntryCallback.Select(context, sector, _ => cached);
            var native = row.GetProperty("events").EnumerateArray().Where(value => value[0].GetString() == "invoke").ToArray();
            Check((index is null ? 0 : 1) == native.Length && (index is null || index == native[0][1].GetUInt16()), "native cached low-word, fallback zero/missing selection");
            Check(WorldScriptSectorEntryCallback.TryInvoke(context, sector, 1, index, selected =>
            {
                Check(context.IsEnteringSector && ReferenceEquals(context.CurrentSector, sector) && context.LocalNetworkSpeedSettings == 1 && context.SpawnEquipment.Count == 0,
                    "native +12, current environment and clear-before-dispatch");
                return true;
            }), "supported callback delivery");
            Check(!context.IsEnteringSector && context.CurrentSector is null && context.LocalNetworkSpeedSettings is null, "caller cleanup");
        }
        var definitions = SacredPopulationDefinitions.Read(ListenPortChecks.Image(proof));
        var thresholds = Bytes(proof, "population_thresholds_hex");
        var balance = new SacredPopulationBalance(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(thresholds),
            System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(thresholds.AsSpan(4)));
        foreach (var row in proof.GetProperty("cases").EnumerateArray())
        {
            var seed = row.GetProperty("seed").GetUInt32();
            var random = new SacredRandomSequence(seed);
            var creatures = new EarlyReturnWorld(random);
            var index = proof.GetProperty("function_index").GetInt32();
            var code = Bytes(proof, "code_hex");
            var functions = Enumerable.Range(0, index + 1).Select(_ => new SacredScriptFunction("reserved", 0, 0, -1, 0, 0)).ToArray();
            functions[index] = new("Sector51038Enter", 0, code.Length, -1, 0, 0);
            var context = new WorldScriptExecutionContext(new(), functions, code, true, random.Next) { CreatureWorld = creatures };
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            // Seed a previous rule to exercise the native erase-before-lookup.
            context.AddSpawnEquipment(SacredScriptSpawnEquipment.Read(SacredCompiledScriptReader.Read(code).Single(command => command.Opcode == 115)));
            var sector = new WorldScriptSectorEnvironment(source);
            var population = new WorldParticipantPopulationCommand(definitions, balance) { ClockMilliseconds = () => row.GetProperty("milliseconds").GetUInt32() };
            Check(WorldScriptSectorEntryCallback.TryInvoke(context, sector, 1, (ushort)index, selected =>
            {
                new WorldScriptExecutor(context, (state, command) =>
                { if (command.Opcode != 51) return false; population.Execute(state, command, 1); return true; }).Invoke(selected);
                return true;
            }), "complete original file body in shared VM");
            Check(sector.ToPayload().SequenceEqual(Bytes(row, "environment_after_hex")), "all mutable environment bytes");
            Check(context.SpawnEquipment.Count == 1 && context.SpawnEquipment.Single().ToPayload().SequenceEqual(Bytes(row, "spawn_equipment_hex")), "complete native 160-byte rule and prior-rule erase");
            Check(random.State == row.GetProperty("rng_after").GetUInt32() && creatures.Count == row.GetProperty("draws").GetInt32(), "native three early facing draws without allocation");
            Check(population.ExecutedCommands == 2 && population.ClearedSchedules == row.GetProperty("clears").GetInt32(), "native two filtered lists and timer/schedule gate");
            Check(context.ExecutedCommands == 7 && !context.IsInvalidated && context.AssociatedCreatureReference == 0, "whole callback command delivery");
        }
        return checks;
        void Check(bool value, string reason) { if (!value) throw new Exception("Host sector Enter: " + reason); checks++; }
    }

    private sealed class EarlyReturnWorld(SacredRandomSequence random) : IWorldScriptCreatureWorld
    {
        private readonly GoldScriptResourceReplica _resources = new();
        public int Count { get; private set; }
        public void Create(WorldScriptExecutionContext context, SacredScriptCommand command)
        {
            _ = random.Next();
            if (!context.IsEnteringSector || !WorldScriptEnteredCreatureGate.CanSkip(context, command,
                    context.LocalNetworkSpeedSettings ?? throw new Exception("Missing current connection setting"), _resources, null))
                throw new Exception("Native fixture must return before any actor factory.");
            Count++;
        }
    }
    private static byte[] Bytes(JsonElement row, string name) => HostSectorInitializationChecks.Bytes(row, name);
}
