using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class MovementCallbackChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Movement callbacks: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-movement-callbacks.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = wire.ToArray();
            var entry = (GoldMovementCallbackEvent)GoldGameEventDecoder.Decode(new(0x1d1, 0, 1, 2, wire));
            Array.Fill(wire, (byte)0xcc);
            var normalized = Encoding.Latin1.GetString(Convert.FromHexString(row.GetProperty("name_hex").GetString()!));
            Check(entry.Name == normalized && entry.FunctionIndex == row.GetProperty("function").GetUInt32(), "native bounded strncpy and unchanged function word");
            Check(entry.Message.Payload.SequenceEqual(expected), "owned wire and suffix");
            var key = Convert.FromHexString(row.GetProperty("key_hex").GetString()!);
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Bank(key, [7, 7]))); world.Apply(entry);
            var groups = world.WorldBanks.MovementCallbacks.Groups;
            var same = row.GetProperty("existing_key").GetBoolean();
            Check(groups.Count == (same ? 1 : 2), "native case-folded existing/new-key decision");
            var group = groups[same ? 0 : 1];
            Check(group.Functions.Last() == new GoldMovementCallbackReference(entry.FunctionIndex, false), "incremental index distinct from bank hashes");
            Check(groups[0].Name == Read(key) && groups[0].Functions.Take(2).All(value => value.IsHash && value.Value == 7), "original key spelling, duplicate registrations and previous values retained");
            Check(world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "registration does not execute a callback or mutate actors");
            world.Clear(); Check(world.WorldBanks.MovementCallbacks.Groups.Count == 0 && world.WorldBanks.MovementCallbacks.PendingChangeCount == 0, "session cleanup");
        }
        foreach (var row in fixture.RootElement.GetProperty("hash_cases").EnumerateArray())
            Check(SacredResourceHash.ComputeDefaultCLocale(Convert.FromHexString(row.GetProperty("name_hex").GetString()!)) == row.GetProperty("hash").GetUInt32(), "native signed-byte hash, overflow, IDIV and C-locale uppercasing");
        foreach (var row in fixture.RootElement.GetProperty("lookups").EnumerateArray())
        {
            var context = Context(row.GetProperty("names_hex").EnumerateArray().Select(value => Encoding.Latin1.GetString(Convert.FromHexString(value.GetString()!))).ToArray());
            var world = new GoldWorldReplica(); var state = world.WorldBanks.MovementCallbacks;
            world.Apply(GoldGameEventDecoder.Decode(Bank(Name("Gate"), [row.GetProperty("hash").GetUInt32()])));
            ConfigureFunctions(world, context.Functions.Select(row => row.Name).ToArray());
            WorldNetworkMovementCallbacks.ApplyPending(state, context);
            Check(context.MovementCallbacks("gATE").Single() == row.GetProperty("index").GetInt32(), "native hash-to-first-index resolution and missing-hash zero fallback");
            Check(state.PendingChangeCount == 0 && context.ExecutedCommands == 0, "consumed changes released without executing functions");
        }
        foreach (var row in fixture.RootElement.GetProperty("bank_cleanup").EnumerateArray())
        {
            var context = Context(["", "GateEnter", "gATEeNTER", "Other", "n\u00e9w"]);
            var world = new GoldWorldReplica();
            var hashes = row.GetProperty("hashes").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
            var bank = row.TryGetProperty("duplicate_hashes", out var duplicate)
                ? BankMany((Name("Gate"), hashes), (Name("gATE"), duplicate.EnumerateArray().Select(value => value.GetUInt32()).ToArray()))
                : Bank(Name("Gate"), hashes);
            world.Apply(GoldGameEventDecoder.Decode(bank));
            ConfigureFunctions(world, context.Functions.Select(value => value.Name).ToArray());
            WorldNetworkMovementCallbacks.ApplyPending(world.WorldBanks.MovementCallbacks, context);
            Check(context.MovementCallbacks("Gate").SequenceEqual(row.GetProperty("indices").EnumerateArray().Select(value => value.GetInt32())), "native empty/1000/1001 vector cleanup and remapping");
        }
        return checks;
    }
    internal static WorldScriptExecutionContext Context(string[] names) => new(new(), names.Select(name => new SacredScriptFunction(name, 0, 0, -1, 0, 0)).ToArray(), Array.Empty<byte>(), true);
    internal static void ConfigureFunctions(GoldWorldReplica world, string[] names)
    {
        var bytes = new byte[4+names.Length*84]; BinaryPrimitives.WriteInt32LittleEndian(bytes, names.Length);
        for (var index = 0; index < names.Length; index++) Encoding.Latin1.GetBytes(names[index]).CopyTo(bytes, 4+index*84);
        world.WorldBanks.Functions.Configure(bytes);
    }
    internal static byte[] Name(string value)
    {
        var bytes = new byte[128]; Encoding.Latin1.GetBytes(value).CopyTo(bytes, 0); return bytes;
    }
    private static string Read(byte[] bytes) => Encoding.Latin1.GetString(bytes.AsSpan(0, Array.IndexOf(bytes, (byte)0)));
    internal static GoldMessage Bank(byte[] name, uint[] values) => BankMany((name, values));
    internal static GoldMessage BankMany(params (byte[] Name, uint[] Values)[] rows)
    {
        var bytes = new byte[40+rows.Sum(row => 132+row.Values.Length*4)]; BinaryPrimitives.WriteInt32LittleEndian(bytes, rows.Length);
        var cursor = 40;
        foreach (var row in rows)
        {
            row.Name.CopyTo(bytes, cursor); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor+128), row.Values.Length); cursor += 132;
            foreach (var value in row.Values) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value); cursor += 4; }
        }
        return new(0x1b2, 0, 1, 0, bytes);
    }
}
