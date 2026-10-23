using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class TriggerPatchChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Trigger patches: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-trigger-patches.json")));
        var caseIndex = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); var state = world.WorldBanks.TriggerPatches;
            var beforePositions = Convert.FromHexString(row.GetProperty("before_positions_hex").GetString()!);
            world.Apply(GoldGameEventDecoder.Decode(PositionBank(beforePositions)));
            var beforeMap = row.GetProperty("before_entries").EnumerateArray().Select(pair => (pair[0].GetUInt32(), pair[1].GetUInt32())).ToArray();
            world.Apply(GoldGameEventDecoder.Decode(MapBank(beforeMap)));
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = wire.ToArray();
            var entry = (GoldTriggerPatchEvent)GoldGameEventDecoder.Decode(new(0x1ce, 0, 1, 2, wire));
            Array.Fill(wire, (byte)0xcc); world.Apply(entry);
            var afterMap = row.GetProperty("after_map");
            Check(state.Mappings.Count == afterMap.EnumerateObject().Count(), "native packed mapping size and early exit");
            foreach (var pair in afterMap.EnumerateObject()) Check(state.Mappings[uint.Parse(pair.Name)] == pair.Value.GetUInt32(),
                $"case {caseIndex} native mapping key {pair.Name}: managed {state.Mappings[uint.Parse(pair.Name)]}, native {pair.Value.GetUInt32()}");
            Check(state.MappingEntries.SequenceEqual(row.GetProperty("after_entries").EnumerateArray().Select(pair => (pair[0].GetUInt32(), pair[1].GetUInt32()))), "native in-order entries retain duplicate keys");
            var positions = Convert.FromHexString(row.GetProperty("after_positions_hex").GetString()!);
            Check(state.Positions.Count == positions.Length/16, "native pending-vector length");
            for (var i = 0; i < state.Positions.Count; i++)
            {
                var actual = state.Positions[i].ToPayload(); var native = positions.AsSpan(i*16, 16);
                if (i*16 < beforePositions.Length) Check(actual.AsSpan().SequenceEqual(native), "existing record retains padding and sector");
                else Check(actual.AsSpan(0, 2).SequenceEqual(native[..2]) && actual.AsSpan(4, 9).SequenceEqual(native.Slice(4, 9)), "native written XYZ, byte surface and unresolved sector");
            }
            var calls = new List<string>();
            while (state.TryPeekTile(out var cell))
            {
                if (cell.Surface <= 1) calls.Add($"outdoor:{cell.X}:{cell.Y}:0");
                if (cell.Surface > 0)
                {
                    var level = unchecked((ushort)cell.Surface); calls.Add($"floor:{cell.X}:{cell.Y}:{level}");
                    if (row.GetProperty("indoor_available").GetBoolean() && level <= 2) calls.Add($"tile:{cell.X}:{cell.Y}:{level}");
                }
                state.CompleteTile(cell);
            }
            Check(calls.SequenceEqual(row.GetProperty("lookup_calls").EnumerateArray().Select(call => string.Join(":", call.EnumerateArray().Select(v => v.ToString())))),
                "live work preserves native raw-surface lookup order before byte narrowing");
            Check(entry.Message.Payload.SequenceEqual(expected), "owned full buffer and suffix");
            Check(world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "no trigger callback or outgoing echo");
            world.Clear(); Check(state.Positions.Count == 0 && state.Mappings.Count == 0 && state.PendingTileCount == 0 && state.PendingRoutingCount == 0, "session cleanup");
            caseIndex++;
        }
        return checks;
    }
    internal static GoldMessage PositionBank(byte[] data) => ScriptResourceChecks.Bank(0x1b1, 2,
        Enumerable.Range(0, data.Length/16).Select(i => data.AsSpan(i*16, 16).ToArray()).ToArray());
    internal static GoldMessage MapBank(params (uint Key, uint Value)[] rows) => ScriptResourceChecks.Bank(0x1b3, 2,
        rows.Select(row => ScriptResourceChecks.Pair(row.Key, row.Value)).ToArray());
    internal static GoldMessage Message(int count, params GoldTriggerPatchCell[] cells)
    {
        var bytes = new byte[1208]; BinaryPrimitives.WriteInt32LittleEndian(bytes, count); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 500);
        for (var i = 0; i < cells.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8+i*12), cells[i].X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12+i*12), cells[i].Y);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16+i*12), cells[i].Surface);
        }
        return new(0x1ce, 0, 1, 0, bytes);
    }
}
