using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class ObjectStateChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid object state: " + name);
        }
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-object-state.json")));
        foreach (var sample in native.RootElement.GetProperty("samples").EnumerateArray())
        {
            var memory = Convert.FromHexString(sample.GetProperty("state_hex").GetString()!);
            var bytes = Convert.FromHexString(sample.GetProperty("payload_hex").GetString()!);
            uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(memory.AsSpan(offset));
            var state = GoldObjectBaseState.FromPayload(bytes);
            Check(state.ObjectReference == Word(0x0C) && state.ObjectType == Word(0x10), "native object identity writes");
            Check(state.Flags == Word(0x14), "native flag write including cleared bit");
            Check(state.Sector == BinaryPrimitives.ReadUInt16LittleEndian(memory.AsSpan(0x18)) &&
                state.X == unchecked((int)Word(0x1C)) && state.Y == unchecked((int)Word(0x20)) &&
                state.Surface == memory[0x24], "native unaligned serialized location");
            Check(state.Word28 == Word(0x28) && state.Word34 == Word(0x34) && state.Byte38 == memory[0x38] &&
                state.Owner == memory[0x39] && state.Word3C == Word(0x3C), "native retained fields and owner byte");
            for (var size = 0; size < GoldObjectStateLayout.Size; size++)
                Reject(() => GoldObjectBaseState.FromPayload(bytes.AsSpan(0, size)), "truncated native state");

            var opcode = (ushort)sample.GetProperty("opcode").GetUInt32();
            var hasPosition = opcode is 0x169 or 0x16B or 0x16F;
            var prefix = hasPosition ? 24 : 8;
            var payload = new byte[prefix + bytes.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, state.ObjectReference);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), state.ObjectType);
            if (hasPosition)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), state.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), state.Y);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), state.Surface);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), state.Sector);
            }
            bytes.CopyTo(payload, prefix);
            var message = new GoldMessage(opcode, 0, 1, 0, payload);
            var validMessage = message with { Payload = payload.ToArray() };
            var decoded = (GoldObjectEvent)GoldGameEventDecoder.Decode(message);
            Check(decoded.BaseState == state && decoded.ObjectState.SequenceEqual(bytes), "packet state and opaque subtype retention");
            Check(hasPosition ? decoded.Position == new GoldObjectPosition(state.X, state.Y, state.Surface, state.Sector)
                : decoded.Position is null, "outer location is separate from serialized state");
            payload[prefix + 4] ^= 1;
            Check(decoded.BaseState == state, "decoded object is isolated from mutable packet payload");
            payload[prefix] ^= 1;
            Reject(() => GoldGameEventDecoder.Decode(message), "unsupported stream marker");
            var delivered = 0;
            Reject(() => GoldGameDispatcher.Dispatch([validMessage, decoded.Message with { Payload = prefix == 24
                ? new byte[64] : new byte[48] }], _ => delivered++), "truncated base state in packet");
            Check(delivered == 0, "invalid object batch has no partial delivery");
        }
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-object-locations.json")));
        foreach (var sample in capture.RootElement.GetProperty("samples").EnumerateArray())
        {
            var payload = Convert.FromHexString(sample.GetProperty("prefix_hex").GetString()!);
            var message = new GoldMessage((ushort)sample.GetProperty("opcode").GetUInt32(),
                sample.GetProperty("sender").GetByte(), sample.GetProperty("origin").GetByte(), 0, payload);
            var obj = (GoldObjectEvent)GoldGameEventDecoder.Decode(message);
            Check(obj.BaseState.ObjectReference == sample.GetProperty("reference").GetUInt32() &&
                obj.BaseState.Sector == sample.GetProperty("sector").GetUInt16() &&
                obj.BaseState.X == sample.GetProperty("x").GetInt32() && obj.BaseState.Y == sample.GetProperty("y").GetInt32() &&
                obj.BaseState.Surface == sample.GetProperty("surface").GetByte(), "native wire location fixture");
        }
        Check(GoldGameEventDecoder.Decode(new GoldMessage(0x16D, 0, 0, 0, [1, 2, 3])) is GoldUnknownEvent,
            "untraced create form remains opaque");
        return checks;
    }
}
