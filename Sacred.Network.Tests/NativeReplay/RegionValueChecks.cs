using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.World.Regions;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class RegionValueChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
        GoldMessage Raw(ushort op, uint region, uint selector, uint value)
        {
            var body = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(body, region);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), selector); BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), value);
            return new(op, 0, 1, 0, body);
        }
        GoldWorldBankEvent Bank(byte[] table)
        {
            var payload = new byte[40 + GoldWorldBankGlobals.Size];
            table.CopyTo(payload, 40 + GoldWorldBankGlobals.FirstBlockSize + GoldWorldBankGlobals.SecondBlockSize);
            return new(new(0x1b1, 0, 1, 0, payload), GoldWorldBankSnapshot.FromPayload(0x1b1, payload));
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-region-values.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var region = row.GetProperty("region").GetUInt32(); var index = unchecked((byte)region);
            var before = Convert.FromHexString(row.GetProperty("before_hex").GetString()!); var after = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var bytes = GoldWorldRegionTable.CreateInitial().ToPayload(); before.CopyTo(bytes, index * 24);
            var bank = Bank(bytes); var world = new GoldWorldReplica(); world.Apply(bank);
            var retained = world.Regions.Table!; var revision = world.Revision;
            var message = Raw(row.GetProperty("opcode").GetUInt16(), region, row.GetProperty("selector").GetUInt32(), row.GetProperty("value").GetUInt32());
            var decoded = (GoldWorldRegionValueEvent)GoldGameEventDecoder.Decode(message);
            world.Apply(decoded);
            Check(world.Regions.Table!.GetRecord(index).SequenceEqual(after), $"native region result: op {message.Opcode:X}, region {region}, selector {decoded.Selector}, raw {decoded.RawValue}");
            after.CopyTo(bytes, index * 24);
            Check(world.Regions.Table.ToPayload().SequenceEqual(bytes), "all unmodified region bytes preserved");
            Check(retained.GetRecord(index).SequenceEqual(before) && bank.Snapshot.Globals!.ReadRegions().GetRecord(index).SequenceEqual(before), "retained table and bank immutable");
            Check(world.Revision == revision + 1 && world.CreatureControls.PendingReplies == 0, "one replica update, no owner publication");
            Check(decoded.Region == region && decoded.RegionIndex == index && decoded.ValueKind == unchecked((ushort)decoded.Selector), "raw words retained, native narrowing exposed");
        }
        Check(Marshal.SizeOf<SacredRegionStateLayout>() == 24 && Marshal.OffsetOf<SacredRegionStateLayout>(nameof(SacredRegionStateLayout.Status)).ToInt32() == 2 && Marshal.OffsetOf<SacredRegionStateLayout>(nameof(SacredRegionStateLayout.DynamicFactor)).ToInt32() == 20, "packed mapped layout");
        var session = new GoldWorldReplica();
        void Apply(ushort opcode, uint selector, uint value) => session.Apply(GoldGameEventDecoder.Decode(Raw(opcode, 17, selector, value)));
        Apply(0x187, 1, 0);
        Check(session.Regions.Table!.GetState(17).Status == 0 && float.IsPositiveInfinity(session.Regions.Table.GetState(17).RegionFactor), "native zero status reciprocal, no invented finite fallback");
        Apply(0x187, 1, 127); var factor = session.Regions.Table.GetState(17).RegionFactor;
        Apply(0x188, 1, 65535);
        Check(session.Regions.Table.GetState(17).Status == 126 && session.Regions.Table.GetState(17).RegionFactor == factor, "add wraps signed short before range check and leaves factor unchanged");
        Apply(0x187, 2, 3); Apply(0x188, 2, 2); Apply(0x187, 3, 4); Apply(0x187, 4, 2);
        Check(session.Regions.Table.GetState(17).MerchantFactor == 40, "current regional merchant factor uses all three native fields");
        var flag = GoldWorldRegionFlagsCodec.Create(2, 17, 0x80000001, true);
        session.Apply(GoldGameEventDecoder.Decode(flag)); Apply(0x188, 3, 1);
        Check(session.Regions.Table.GetFlags(17) == 0x80000001 && session.Regions.Table.GetState(17).StoryFactor == 5, "value and flag updates compose");
        session.Apply(new GoldWorldRegionValueEvent(Raw(0x187, 17, 4, 7), 18, 1, 99));
        Check(session.Regions.Table.GetState(17).DynamicFactor == 7 && session.Regions.Table.GetState(18).Status == 0, "reducer uses wire rather than forged typed metadata");
        foreach (ushort op in new ushort[] { 0x187, 0x188 })
        {
            for (var length = 0; length < 20; length++)
            {
                if (length == 12) continue;
                var before = session.Regions.Table;
                try { GoldGameDispatcher.Dispatch([Raw(op, 17, 1, 20), new(op, 0, 1, 0, new byte[length])], session.Apply); throw new Exception("Malformed region batch accepted"); }
                catch (InvalidDataException) { Check(ReferenceEquals(session.Regions.Table, before), "malformed batch rejected before any state mutation"); }
            }
            var message = Raw(op, uint.MaxValue, 0x10002, uint.MaxValue);
            var decoded = (GoldWorldRegionValueEvent)GoldGameEventDecoder.Decode(message); Array.Clear(message.Payload);
            Check(decoded.Region == uint.MaxValue && decoded.Message.Payload.Any(b => b != 0), "decode owns raw payload");
            var outgoing = op == 0x187 ? GoldWorldRegionValueCodec.CreateSet(2, uint.MaxValue, 0x10002, uint.MaxValue) : GoldWorldRegionValueCodec.CreateAdd(2, uint.MaxValue, 0x10002, uint.MaxValue);
            Check(BitConverter.ToUInt32(outgoing.Payload) == 255 && BitConverter.ToUInt32(outgoing.Payload, 4) == 2 && BitConverter.ToUInt32(outgoing.Payload, 8) == uint.MaxValue && outgoing is { Sender: 2, Origin: 2 }, "producer masks native region/selector but retains unsigned value");
            foreach (byte slot in new byte[] { 0, 17, 255 })
            {
                try { GoldWorldRegionValueCodec.CreateSet(slot, 1, 1, 1); throw new Exception("Invalid slot accepted"); }
                catch (ArgumentOutOfRangeException) { checks++; }
            }
        }
        var replacement = Bank(GoldWorldRegionTable.CreateInitial().ToPayload()); session.Apply(replacement);
        Check(session.Regions.Table!.GetState(17) is { Status: 0, RegionFactor: 1, StoryFactor: 1, DynamicFactor: 1 }, "later bank replaces incremental state");
        Apply(0x188, 3, 1);
        Check(replacement.Snapshot.Globals!.ReadRegions().GetState(17).StoryFactor == 1 && session.Regions.Table.GetState(17).StoryFactor == 2, "later increment preserves imported bank bytes");
        session.Clear(); Check(session.Regions.Table is null, "disconnect clears values");
        Apply(0x187, 0, 99);
        Check(session.Regions.Table!.GetState(17).Status == 0 && session.Regions.Table.GetState(17).RegionFactor == 1, "unknown selector still constructs singleton and changes no field");
        Console.WriteLine($"PASS: {checks} region value checks."); return checks;
    }
}
