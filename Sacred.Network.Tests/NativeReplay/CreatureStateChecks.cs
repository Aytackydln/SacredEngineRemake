using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class CreatureStateChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid creature state: " + name);
        }
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-state.json")));
        foreach (var sample in native.RootElement.GetProperty("samples").EnumerateArray())
        {
            var memory = Convert.FromHexString(sample.GetProperty("state_hex").GetString()!);
            var bytes = Convert.FromHexString(sample.GetProperty("payload_hex").GetString()!);
            uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(memory.AsSpan(offset));
            ushort Short(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(memory.AsSpan(offset));
            void Vector(GoldPhysicalVector vector, int offset) => Check(
                BitConverter.SingleToUInt32Bits(vector.X) == Word(offset) &&
                BitConverter.SingleToUInt32Bits(vector.Y) == Word(offset + 4) &&
                BitConverter.SingleToUInt32Bits(vector.Z) == Word(offset + 8), "native physical vector bits");

            var physical = GoldPhysicalState.FromPayload(bytes);
            Check(physical.Word64 == Word(0x64) && physical.WordF0 == Word(0xF0) && physical.Word60 == Word(0x60) &&
                physical.Word68 == Word(0x68) && physical.Word6C == Word(0x6C), "native physical words");
            Vector(physical.Vector70, 0x70);
            Vector(physical.VectorD8, 0xD8);
            Vector(physical.VectorE4, 0xE4);
            Vector(physical.SelectedVector, (Word(0x14) & 0x800000) != 0 ? 0xCC : 0x98);
            Check(physical.UsesVectorCC == ((Word(0x14) & 0x800000) != 0) && physical.Word94 == Short(0x94) &&
                physical.Word96 == Short(0x96) && physical.WordB0 == Word(0xB0), "native physical branch and remaining fields");
            Check(physical.ToPayload().SequenceEqual(bytes) && physical.RemainingData.SequenceEqual(bytes.AsSpan(121)), "physical lossless tail");

            var creature = GoldCreatureState.FromPayload(bytes);
            Check(creature.EquipmentReferences.SequenceEqual(Enumerable.Range(0, 19).Select(i => Word(0x1A4 + i * 4)).ToArray()),
                "native equipment array with zero and unresolved references");
            Check(creature.Word1F0 == Word(0x1F0) && creature.Word1F4 == Word(0x1F4) && creature.Word1F8 == Word(0x1F8) &&
                creature.Byte1FC == memory[0x1FC] && creature.Byte1FD == memory[0x1FD] && creature.Word1FE == Short(0x1FE) &&
                creature.Word200 == Word(0x200), "native creature header words and unaligned fields");
            Check(new[] { creature.Word245, creature.Word249, creature.Word24D, creature.Word251, creature.Word255, creature.Word259 }
                .SequenceEqual(new[] { Word(0x245), Word(0x249), Word(0x24D), Word(0x251), Word(0x255), Word(0x259) }), "native creature six-word block");
            Check(creature.Word25F == Short(0x25F) && creature.Word261 == Short(0x261) && creature.Byte2B6 == memory[0x2B6] &&
                creature.Word2B7 == Short(0x2B7) && creature.Word2C9 == Word(0x2C9), "native creature short fields");
            Check(creature.Block2B9.SequenceEqual(memory.AsSpan(0x2B9, 16)) && creature.Block2ED.SequenceEqual(memory.AsSpan(0x2ED, 173)),
                "native creature fixed retained blocks");
            Check(creature.ToPayload().SequenceEqual(bytes) && creature.RemainingData.Length == 0 &&
                creature.PhysicalState.ToPayload().SequenceEqual(bytes), "creature prefix round trip");

            for (var size = 0; size < GoldCreatureStateLayout.PrefixSize; size++)
                Reject(() => GoldCreatureState.FromPayload(bytes.AsSpan(0, size)), "truncated creature prefix");
            for (var size = 0; size < GoldPhysicalStateLayout.Size; size++)
                Reject(() => GoldPhysicalState.FromPayload(bytes.AsSpan(0, size)), "truncated physical prefix");

            var opcode = (ushort)sample.GetProperty("opcode").GetUInt32();
            var prefixSize = opcode is 0x169 or 0x16B or 0x16F ? 24 : 8;
            var packet = new byte[prefixSize + bytes.Length + 7];
            bytes.CopyTo(packet, prefixSize);
            packet[^1] = 0xEE;
            var message = new GoldMessage(opcode, 0, 1, 0, packet);
            var obj = (GoldObjectEvent)GoldGameEventDecoder.Decode(message);
            Check(obj.CreatureState is { } decodedCreature && decodedCreature.EquipmentReferences.SequenceEqual(creature.EquipmentReferences) &&
                decodedCreature.RemainingData.Length == 7 && decodedCreature.RemainingData[^1] == 0xEE && obj.PhysicalState is not null,
                "layered event keeps variable tail");
            packet[prefixSize + 125] ^= 1;
            Check(obj.CreatureState!.EquipmentReferences.SequenceEqual(creature.EquipmentReferences), "event copies packet input");
            var saved = creature.ToPayload();
            bytes[125] ^= 1;
            Check(creature.ToPayload().SequenceEqual(saved), "reader copies source input");
            var exported = creature.ToPayload();
            exported[125] ^= 1;
            Check(creature.ToPayload().SequenceEqual(saved), "reader copies output");

            foreach (var markerOffset in new[] { 0, 41, 121 })
            {
                var invalid = saved.ToArray(); invalid[markerOffset] ^= 1;
                Reject(() => GoldCreatureState.FromPayload(invalid), "invalid layered marker");
            }
            foreach (var size in new[] { 45, 120, 125, 444 })
            {
                var delivered = 0;
                var truncated = message with { Payload = packet[..(prefixSize + size)] };
                Reject(() => GoldGameDispatcher.Dispatch([obj.Message, truncated], _ => delivered++), "recognized truncated subtype in batch");
                Check(delivered == 0, "no partial subtype dispatch");
            }
            var baseOnly = obj.ObjectState[..41];
            var otherSubtype = baseOnly.Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray();
            Check(GoldObjectStateReader.ReadPhysical(otherSubtype) is null && GoldObjectStateReader.ReadCreature(otherSubtype) is null,
                "unrecognized subtype stays opaque");
            Check(GoldObjectStateReader.ReadCreature(saved.AsSpan(0, 121)) is null && GoldObjectStateReader.ReadPhysical(saved.AsSpan(0, 121)) is not null,
                "physical object does not imply creature");
        }
        return checks;
    }
}
