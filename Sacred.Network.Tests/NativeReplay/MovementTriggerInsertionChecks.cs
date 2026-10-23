using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.Network.Gameplay;

internal static class MovementTriggerInsertionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Movement-trigger insertion: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-movement-trigger-insertion.json")));
        var grids = fixture.RootElement.GetProperty("grids").EnumerateArray().Select(grid => Index(grid.EnumerateArray().Select(row =>
            (row.GetProperty("id").GetUInt32(), row.GetProperty("tile_x").GetInt32(), row.GetProperty("tile_y").GetInt32())))).ToArray();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            var expected = wire.ToArray(); var entry = (GoldMovementTriggerInsertionEvent)GoldGameEventDecoder.Decode(new(0x1c3, 0, 1, 2, wire));
            Array.Fill(wire, (byte)0xcc);
            Check(entry.Insertion.ToPayload().SequenceEqual(expected) && entry.Message.Payload.SequenceEqual(expected), "owned full 232-byte record");
            var world = new GoldWorldReplica();
            var before = Convert.FromHexString(row.GetProperty("before_name_hex").GetString()!);
            world.Apply(GoldGameEventDecoder.Decode(ScriptResourceChecks.Bank(0x1b2, 1, [before])));
            world.MovementTriggers.ConfigureSectors(grids[row.GetProperty("grid").GetInt32()]); world.Apply(entry);
            Check(world.MovementTriggers.Names.SelectMany(n => n.ToPayload()).SequenceEqual(
                Convert.FromHexString(row.GetProperty("name_vector_hex").GetString()!)), "native append retains all trailer bytes after existing registration");
            Check(world.MovementTriggers.AppliedInsertionCount == 1 && world.MovementTriggers.PendingInsertionCount == 0, "completed routing");
            if (row.GetProperty("mode").GetString() == "client")
            {
                var planned = GoldMovementTriggerPlanner.CreateReceived(entry.Insertion, grids[row.GetProperty("grid").GetInt32()].CreateGrid());
                var pieces = row.GetProperty("pieces").EnumerateArray().ToArray();
                Check(planned.Count == pieces.Length, "native recursive piece count, including grid holes");
                for (var i = 0; i < pieces.Length; i++)
                {
                    var bytes = Convert.FromHexString(pieces[i].GetProperty("native_record_hex").GetString()!);
                    Check(planned[i].Sector == pieces[i].GetProperty("sector").GetUInt32(), "native recursive append order");
                    Check(planned[i].Record.ToPayload().AsSpan(0, 10).SequenceEqual(bytes.AsSpan(0, 10)), "written coordinates and raw low-word surface");
                    Check(planned[i].Record.NameBytes.SequenceEqual(bytes.AsSpan(12, entry.Insertion.NameBytes.Length)), "written name bytes");
                    var actual = world.MovementTriggers.Sectors[planned[i].Sector].Records.Single();
                    Check(actual.ToPayload().SequenceEqual(planned[i].Record.ToPayload()), "received sector state uses native routing");
                }
            }
            Check(world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "no actor callback or publication");
            world.Clear();
            Check(world.MovementTriggers.Names.Count == 0 && world.MovementTriggers.Sectors.Count == 0 && world.MovementTriggers.AppliedInsertionCount == 0, "session cleanup");
        }
        foreach (var length in fixture.RootElement.GetProperty("rejected_client_lengths").EnumerateArray())
        {
            try { GoldGameEventDecoder.Decode(new(0x1c3, 0, 1, 0, new byte[length.GetInt32()])); }
            catch (InvalidDataException) { checks++; continue; }
            throw new Exception("Accepted native rejected length");
        }
        return checks;
    }

    internal static SacredSectorIndex Index(IEnumerable<(uint Id, int X, int Y)>? rows = null)
    {
        rows ??= from y in Enumerable.Range(0, 3) from x in Enumerable.Range(0, 3) select ((uint)(1+y*3+x), x*64, y*64);
        var bytes = new byte[256+9*768]; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 9);
        var i = 0;
        foreach (var row in rows)
        {
            var record = bytes.AsSpan(256+i++*768, 768);
            BinaryPrimitives.WriteUInt32LittleEndian(record[36..], row.Id);
            BinaryPrimitives.WriteInt32LittleEndian(record[60..], (int)Math.Ceiling(row.X*(double)SacredSectorCoordinates.WorldUnitsPerTile)-25);
            BinaryPrimitives.WriteInt32LittleEndian(record[64..], (int)Math.Ceiling(row.Y*(double)SacredSectorCoordinates.WorldUnitsPerTile)-25);
        }
        return SacredSectorIndex.FromPayload(bytes);
    }
}
