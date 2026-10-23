using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class ScriptSpatialChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Spatial scripts: " + why); checks++; }
        GoldGameEvent Event(ushort op, byte[] payload) => GoldGameEventDecoder.Decode(new(op, 0, 1, 2, payload));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-spatial.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var op = row.GetProperty("opcode").GetUInt16();
            var world = new GoldWorldReplica(); var spatial = world.WorldBanks.Spatial;
            var seed = Convert.FromHexString(row.GetProperty("initial_hex").GetString()!);
            for (var i = 0; i < seed.Length; i += 24) world.Apply(Event(op, seed.AsSpan(i, 24).ToArray()));
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            var copy = wire.ToArray(); var received = Event(op, wire); Array.Fill(wire, (byte)0xee);
            var revision = world.Revision; world.Apply(received);
            var records = op == 0x1cb ? spatial.MapIcons.Select(icon => icon.ToPayload()).ToArray() : spatial.Attractors.Select(a => a.ToPayload()).ToArray();
            Check(records.Length == row.GetProperty("count").GetInt32(), "native append count");
            Check(records.Take(records.Length - 1).SelectMany(r => r).SequenceEqual(seed), "previous records unchanged");
            Check(records[^1].SequenceEqual(Convert.FromHexString(row.GetProperty("row_hex").GetString()!)), "all 24 bytes preserved without normalization");
            Check(received.Message.Payload.SequenceEqual(copy), "decoded event owns suffix bytes");
            Check(world.Revision == revision + 1 && world.CreatureControls.PendingReplies == 0, "accepted insertion revision; no owner reply");
            records[^1][0] ^= 255;
            Check((op == 0x1cb ? spatial.MapIcons[^1].ToPayload() : spatial.Attractors[^1].ToPayload())[0] != records[^1][0], "exported bytes cannot mutate current record");
            world.Clear(); Check(spatial.MapIcons.Count == 0 && spatial.Attractors.Count == 0 && !spatial.HasMapState, "disconnect clears both tables");
        }
        foreach (var row in fixture.RootElement.GetProperty("map_filters").EnumerateArray())
        {
            var world = new GoldWorldReplica(); var resources = row.GetProperty("resources").EnumerateArray().Select(x => x.GetUInt32()).ToArray();
            for (var i = 0; i < resources.Length; i++)
            {
                var record = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)i);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 123); BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), 456);
                record[16] = 128; record[17] = 255; BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(20), resources[i]);
                world.Apply(Event(0x1cb, record));
            }
            Check(world.WorldBanks.Spatial.MapIcons.Where(i => i.IsLabel).SelectMany(i => i.ToPayload()).SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)), "native map-name filtering/order/duplicates");
        }
        foreach (var op in new ushort[] { 0x1cb, 0x1cc })
        for (var length = 0; length < 24; length++)
        {
            try { Event(op, new byte[length]); throw new Exception("short spatial record accepted"); }
            catch (InvalidDataException) { checks++; }
        }
        var table = new GoldWorldReplica(); var data = Enumerable.Repeat((byte)'Z', 24).ToArray();
        table.Apply(Event(0x1cb, data)); table.Apply(Event(0x1cb, data)); table.Apply(Event(0x1cc, data));
        GoldGameEvent Bank(byte[] bytes) => Event(0x1b4, bytes);
        table.Apply(Bank(new byte[40])); Check(table.WorldBanks.Spatial.MapIcons.Count == 2, "native zero-count map bank preserves existing records");
        foreach (var count in new[] { 1, 3 })
        {
            var bytes = new byte[40 + count*24]; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)count);
            for (var i = 40; i < bytes.Length; i++) bytes[i] = (byte)i;
            table.Apply(Bank(bytes));
            Check(table.WorldBanks.Spatial.MapIcons.Count == count && table.WorldBanks.Spatial.Attractors.Count == 1, "nonempty bank replaces map table alone");
            Check(table.WorldBanks.Spatial.MapIcons.SelectMany(i => i.ToPayload()).SequenceEqual(bytes[40..]), "bank section 2 exact mapping");
            var snapshot = table.WorldBanks.Current[0x1b4].ToPayload();
            table.Apply(Event(0x1cb, data)); Check(table.WorldBanks.Current[0x1b4].ToPayload().SequenceEqual(snapshot), "incremental state does not rewrite captured bank");
        }
        var packed = new byte[24]; BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(4), int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(8), -7); packed[16] = 240; packed[17] = 220;
        var icon = GoldMapIconState.FromPayload(packed);
        Check(icon.X == int.MinValue && icon.Y == -7 && icon.Curvature == -16 && icon.AngleDegrees == -36, "signed tile coordinates and packed signed label transforms");
        Check(GoldScriptSpatialCodec.CreateMapIcon(3, icon).Payload.SequenceEqual(packed) && GoldScriptSpatialCodec.CreateAttractor(3, GoldAttractorState.FromPayload(packed)).Payload.SequenceEqual(packed), "native exact-size producers");
        foreach (var slot in new byte[] { 0, 17, 255 })
        {
            try { GoldScriptSpatialCodec.CreateMapIcon(slot, icon); throw new Exception("invalid publisher slot"); }
            catch (ArgumentOutOfRangeException) { checks++; }
            try { GoldScriptSpatialCodec.CreateAttractor(slot, GoldAttractorState.FromPayload(packed)); throw new Exception("invalid publisher slot"); }
            catch (ArgumentOutOfRangeException) { checks++; }
        }
        Console.WriteLine($"PASS: {checks} spatial script packet checks"); return checks;
    }
}
