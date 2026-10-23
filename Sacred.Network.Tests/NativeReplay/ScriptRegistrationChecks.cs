using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class ScriptRegistrationChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Script registrations: " + why); checks++; }
        GoldGameEvent Event(ushort op, byte[] data) => GoldGameEventDecoder.Decode(new(op, 0, 1, 2, data));
        byte[] Bytes(JsonElement row, string field) => Convert.FromHexString(row.GetProperty(field).GetString()!);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-registrations.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var op = row.GetProperty("opcode").GetUInt16();
            if (row.GetProperty("mode").GetString() == "server" && op == 0x1d0)
            {
                Check(row.GetProperty("relays").GetArrayLength() == 1, "server relays mouseovers even for duplicate keys");
                continue; // Server appends; the managed replica represents the client priority order.
            }
            var world = new GoldWorldReplica(); var state = world.WorldBanks.Registrations;
            var seed = Bytes(row, "initial_hex");
            if (op == 0x1cf)
                for (var i = 0; i < seed.Length; i += 60) world.Apply(Event(op, seed.AsSpan(i, 60).ToArray()));
            else
            {
                for (var i = 0; i < seed.Length; i += 88) state.Mouseovers.Register(GoldSectorScriptPatch.FromPayload(seed.AsSpan(i, 88)));
                world.Apply(Event(0x1b3, Bank(5, 88, Bytes(row, "pending_hex"))));
            }
            if (row.GetProperty("entered").GetBoolean())
                world.BeginSectorInterest(GoldSectorInterestRequest.Create(1, 0, new ushort[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, new ushort[9], 0));
            var wire = Bytes(row, "payload_hex"); var expectedWire = wire.ToArray();
            var entry = Event(op, wire); Array.Fill(wire, (byte)0xee); var revision = world.Revision;
            world.Apply(entry);
            var actual = op == 0x1cf ? state.SpawnRenewals.SelectMany(r => r.ToPayload()).ToArray()
                : state.Mouseovers.Active.SelectMany(r => r.ToPayload()).ToArray();
            Check(actual.SequenceEqual(Bytes(row, "result_hex")), "native active vector bytes and priority order");
            Check(state.PendingMouseovers.SelectMany(p => p.ToPayload()).SequenceEqual(Bytes(row, "pending_result_hex")), "native pending vector preserves first payload independently");
            Check(entry.Message.Payload.SequenceEqual(expectedWire), "record and ignored suffix are owned");
            Check(world.Revision == revision+1 && world.CreatureControls.PendingReplies == 0 && world.Objects.Count == 0, "revision without object/callback effects");
            if (op == 0x1d0)
            {
                Check(state.TryPeekTile(out var work) && work!.EnteredSectors.Contains(1) == row.GetProperty("entered").GetBoolean(), "capture native entered flag for deferred tile mark");
                state.CompleteTile(work!); Check(state.PendingTileCount == 0, "tile work completes in receipt order");
            }
            world.Clear();
            Check(state.SpawnRenewals.Count == 0 && state.PendingMouseovers.Count == 0 && state.Mouseovers.Active.Count == 0 && state.PendingTileCount == 0, "disconnect clears registrations and work");
        }
        var table = new GoldWorldReplica(); var registrations = table.WorldBanks.Registrations;
        var spawn = Enumerable.Range(0, 60).Select(i => (byte)i).ToArray();
        table.Apply(Event(0x1b2, Bank(6, 60, [..spawn, ..spawn])));
        Check(registrations.SpawnRenewals.Count == 2, "bank imports duplicate spawn-renew records in order");
        table.Apply(Event(0x1b2, new byte[40]));
        Check(registrations.SpawnRenewals.Count == 2, "zero bank count preserves spawn vector");
        table.Apply(Event(0x1cf, spawn));
        Check(registrations.SpawnRenewals.Count == 3 && table.WorldBanks.Current[0x1b2].PayloadSize == 40, "incremental spawn append keeps raw bank unchanged");
        table.Apply(Event(0x1b2, Bank(6, 60, spawn)));
        Check(registrations.SpawnRenewals.Count == 1, "nonempty bank replaces spawn vector");
        var patch = new byte[88];
        var forged = new GoldMouseoverEvent(new(0x1d0, 0, 1, 0, patch), GoldSectorScriptPatch.FromPayload(Enumerable.Repeat((byte)255, 88).ToArray()));
        table.Apply(forged);
        Check(registrations.Mouseovers.Active[0].X == 0, "wire bytes override forged parsed metadata");
        table.Apply(Event(0x1b3, new byte[40]));
        Check(registrations.PendingMouseovers.Count == 1, "zero bank count preserves pending mouseovers");
        patch[0] = 2; table.Apply(Event(0x1b3, Bank(5, 88, patch)));
        Check(registrations.PendingMouseovers.Single().X == 2 && registrations.IncrementalMouseovers.Count == 0 && registrations.Mouseovers.Active.Single().X == 0, "nonempty pending bank replaces pending alone");
        var createdSpawn = GoldScriptRegistrationCodec.CreateSpawnRenew(2, GoldSpawnRenewRecord.FromPayload(spawn));
        var createdMouseover = GoldScriptRegistrationCodec.CreateMouseover(2, GoldSectorScriptPatch.FromPayload(patch));
        Check(createdSpawn.Sender == 2 && createdSpawn.Origin == 2 && createdSpawn.Payload.SequenceEqual(spawn), "spawn native producer payload");
        Check(createdMouseover.Sender == 2 && createdMouseover.Origin == 2 && createdMouseover.Payload.SequenceEqual(patch), "mouseover native producer payload");
        foreach (var slot in new byte[] { 0, 17, 255 })
        {
            try { GoldScriptRegistrationCodec.CreateSpawnRenew(slot, GoldSpawnRenewRecord.FromPayload(spawn)); throw new Exception("invalid slot accepted"); }
            catch (ArgumentOutOfRangeException) { checks++; }
            try { GoldScriptRegistrationCodec.CreateMouseover(slot, GoldSectorScriptPatch.FromPayload(patch)); throw new Exception("invalid slot accepted"); }
            catch (ArgumentOutOfRangeException) { checks++; }
        }
        return checks;
    }
    private static byte[] Bank(int section, int stride, byte[] data)
    {
        var bytes = new byte[40+data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(section*4), (uint)(data.Length/stride));
        data.CopyTo(bytes, 40); return bytes;
    }
}
