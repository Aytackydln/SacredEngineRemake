using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class CreatureValuesChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid creature values: " + name);
        }
        void RejectIndex(Action action)
        {
            try { action(); } catch (ArgumentOutOfRangeException) { checks++; return; }
            throw new Exception("Accepted invalid creature record index");
        }
        using var prefixes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-state.json")));
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-values.json")));
        foreach (var sample in fixtures.RootElement.GetProperty("samples").EnumerateArray())
        {
            var opcode = (ushort)sample.GetProperty("opcode").GetUInt32();
            var memory = Convert.FromHexString(sample.GetProperty("state_hex").GetString()!);
            var bytes = Convert.FromHexString(sample.GetProperty("values_hex").GetString()!);
            var tailBytes = Convert.FromHexString(sample.GetProperty("tail_hex").GetString()!);
            var values = GoldCreatureValuesState.FromPayload(bytes, opcode);
            uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(memory.AsSpan(offset));
            ushort Short(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(memory.AsSpan(offset));
            Check(values.IsFull == !sample.GetProperty("compact").GetBoolean(), "native opcode selects compact/full values");
            Check(values.Block128.SequenceEqual(memory.AsSpan(0x128, 24)), "native values +0x128 block");
            Check(values.IsFull ? values.FullBlock08.SequenceEqual(memory.AsSpan(8, 238)) : values.FullBlock08.Length == 0,
                "native full +0x08 block");
            Check(values.Word0C == Word(0x0C) && values.Word46 == Word(0x46) && values.Word56 == Short(0x56), "native values words");
            Check(values.Block24.SequenceEqual(memory.AsSpan(0x24, 8)) && values.Block2C.SequenceEqual(memory.AsSpan(0x2C, 8)), "native values eight-byte blocks");
            Check(values.Word42 == Short(0x42) && values.Word44 == Short(0x44) && values.Block1C.SequenceEqual(memory.AsSpan(0x1C, 8)) &&
                values.Byte58 == memory[0x58] && values.Word148 == Word(0x148), "native compact suffix/full field parity");
            var records22 = Convert.FromHexString(sample.GetProperty("records22_hex").GetString()!);
            var records38 = Convert.FromHexString(sample.GetProperty("records38_hex").GetString()!);
            Check(values.Count22 == sample.GetProperty("count22").GetUInt16() && values.Count38 == sample.GetProperty("count38").GetUInt16() &&
                values.Records22.SequenceEqual(records22) && values.Records38.SequenceEqual(records38), "native value record arrays and counts");
            for (var i = 0; i < values.Count22; i++) Check(values.GetRecord22(i).SequenceEqual(records22.AsSpan(i * 22, 22)), "22-byte record boundary");
            for (var i = 0; i < values.Count38; i++) Check(values.GetRecord38(i).SequenceEqual(records38.AsSpan(i * 38, 38)), "38-byte record boundary");
            RejectIndex(() => values.GetRecord22(-1)); RejectIndex(() => values.GetRecord22(values.Count22));
            RejectIndex(() => values.GetRecord38(-1)); RejectIndex(() => values.GetRecord38(values.Count38));
            Check(values.SerializedSize == bytes.Length && values.RemainingData.Length == 0 && values.ToPayload().SequenceEqual(bytes), "values exact native boundary");
            for (var size = 0; size < bytes.Length; size++)
                Reject(() => GoldCreatureValuesState.FromPayload(bytes.AsSpan(0, size), opcode), "truncated value/list stream");

            var tail = GoldCreatureTail.FromPayload(tailBytes, opcode);
            var records9 = Convert.FromHexString(sample.GetProperty("records9_hex").GetString()!);
            Check(tail.Values.ToPayload().SequenceEqual(bytes) && tail.Count == sample.GetProperty("count9").GetUInt16() &&
                tail.Records.SequenceEqual(records9), "native nested values and nine-byte records");
            for (var i = 0; i < tail.Count; i++) Check(tail.GetRecord(i).SequenceEqual(records9.AsSpan(i * 9, 9)), "nine-byte record boundary");
            RejectIndex(() => tail.GetRecord(-1)); RejectIndex(() => tail.GetRecord(tail.Count));
            Check(tail.Mode == tailBytes[^11] && tail.Word570 == BinaryPrimitives.ReadUInt32LittleEndian(tailBytes.AsSpan(tailBytes.Length - 10)) &&
                tail.Word574 == BinaryPrimitives.ReadUInt16LittleEndian(tailBytes.AsSpan(tailBytes.Length - 6)) &&
                tail.Word578 == BinaryPrimitives.ReadUInt32LittleEndian(tailBytes.AsSpan(tailBytes.Length - 4)), "native trailer fields");
            Check(tail.SerializedSize == tailBytes.Length && tail.RemainingData.Length == 0 && tail.ToPayload().SequenceEqual(tailBytes), "tail exact native boundary");
            for (var size = 0; size < tailBytes.Length; size++)
                Reject(() => GoldCreatureTail.FromPayload(tailBytes.AsSpan(0, size), opcode), "truncated creature tail");
            var input = tailBytes.Concat(new byte[] { 0xDD, 0xEE }).ToArray();
            var copied = GoldCreatureTail.FromPayload(input, opcode);
            input[4] ^= 1;
            var exported = copied.ToPayload(); exported[4] ^= 1;
            Check(copied.ToPayload().SequenceEqual(tailBytes.Concat(new byte[] { 0xDD, 0xEE })) && copied.RemainingData.SequenceEqual(new byte[] { 0xDD, 0xEE }), "tail copy isolation and unknown continuation");
            var changed = tailBytes.ToArray(); changed[^11] = 255;
            Check(GoldCreatureTail.FromPayload(changed, opcode).Mode == 255, "mode is retained without invented validity policy");
            changed = tailBytes.ToArray(); changed[0] ^= 1;
            Reject(() => GoldCreatureTail.FromPayload(changed, opcode), "bad values marker");
            foreach (var offset in new[] { values.IsFull ? 266 : 54, values.SerializedSize - (values.IsFull ? 4 : 17) - records38.Length - 2, values.SerializedSize })
            {
                changed = tailBytes.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(offset), ushort.MaxValue);
                Reject(() => GoldCreatureTail.FromPayload(changed, opcode), "oversized counted list");
            }

            var prefix = prefixes.RootElement.GetProperty("samples").EnumerateArray().First(x => x.GetProperty("opcode").GetUInt32() == opcode);
            var fixedBytes = Convert.FromHexString(prefix.GetProperty("payload_hex").GetString()!);
            var packetPrefixSize = opcode is 0x169 or 0x16B or 0x16F ? 24 : 8;
            var payload = new byte[packetPrefixSize].Concat(fixedBytes).Concat(tailBytes).ToArray();
            var message = new GoldMessage(opcode, 0, 0, 0, payload);
            var obj = (GoldObjectEvent)GoldGameEventDecoder.Decode(message);
            Check(obj.CreatureTail!.ToPayload().SequenceEqual(tailBytes), "event parses complete native-shaped tail");
            var approval = new GoldMessage(0x11, 0, 0, 1, [0, 2, 0, 0, 0, 0]);
            for (var size = 4; size < tailBytes.Length; size++)
            {
                var truncated = message with { Payload = payload[..(packetPrefixSize + fixedBytes.Length + size)] };
                Reject(() => GoldGameEventDecoder.Decode(truncated), "truncated recognized tail in object packet");
            }
            var bad = message with { Payload = payload[..^1] };
            var delivered = 0;
            Reject(() => GoldGameDispatcher.Dispatch([message, bad], _ => delivered++), "invalid tail batch");
            Check(delivered == 0, "tail failure has no partial event delivery");
            var tracker = new GoldAdmissionTracker(); tracker.Begin();
            Reject(() => tracker.ProcessBatch([approval, bad]), "invalid tail following approval");
            Check(tracker.Status.Phase == GoldAdmissionPhase.WaitingForResponse, "tail failure cannot partly assign slot");
        }
        RejectIndex(() => GoldCreatureValuesState.FromPayload(new byte[100], 0xFFFF));
        return checks;
    }
}
