using System.Buffers.Binary;
using Sacred.Network.Admission;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold dispatch only. The Demo NW enum and lobby IDs belong to other protocols.</summary>
public static class GoldGameEventDecoder
{
    public static GoldGameEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = message.Payload.AsSpan();
        switch (message.Opcode)
        {
            case GoldFirstContactLayout.Opcode:
                return new GoldFirstContactEvent(message, GoldFirstContact.FromPayload(payload));
            case GoldPlayerDataLayout.Opcode:
                if (payload.Length == GoldPlayerRosterLayout.Size)
                    return new GoldPlayerRosterEvent(message, GoldPlayerRoster.FromPayload(payload));
                return new GoldPlayerDataEvent(message, GoldPlayerData.FromPayload(payload));
            case 0x11:
                RequireExact(payload, 6, message.Opcode);
                if (payload[1] is < 1 or > 16) throw new InvalidDataException("Invalid Sacred player slot.");
                return new GoldAdmissionEvent(message, payload[0], payload[1], Word(payload, 2));
            case 0x3F:
                RequireExact(payload, 8, message.Opcode);
                return new GoldRejectionEvent(message, Word(payload, 0), Word(payload, 4));
            case 0x13:
            case 0x14:
                RequireExact(payload, 0, message.Opcode);
                return new GoldPasswordResultEvent(message, message.Opcode == 0x14);
            case GoldTimeProbeCodec.RequestOpcode:
                RequireExact(payload, GoldTimeProbeCodec.RequestBytes, message.Opcode);
                return new GoldTimeProbeEvent(message, Word(payload, 0), Word(payload, 4), Word(payload, 8), Word(payload, 12));
            case GoldReferenceCodec.BatchOpcode:
                if (message.Sender == 0)
                    return new GoldReferenceBatchEvent(message, GoldReferenceBatch.FromPayload(payload));
                RequireExact(payload, 4, message.Opcode);
                var count = Word(payload, 0);
                if (message.Sender > 16 || count is 0 or >= GoldReferenceCodec.ReferenceLimit)
                    throw new InvalidDataException("Invalid Gold object-reference request.");
                return new GoldReferenceRequestEvent(message, count);
            case GoldReferenceCodec.RangeOpcode:
                RequireExact(payload, 8, message.Opcode);
                return new GoldReferenceRangeEvent(message, Word(payload, 0), Word(payload, 4));
            // GameServer 0x4962A0 (start/update), 0x496AB0 (end).
            case 0x161:
            case 0x163:
                Require(payload, 24, message.Opcode);
                return new GoldMovementEvent(message, Word(payload, 0), Word(payload, 4), Word(payload, 8),
                    Word(payload, 12), Word(payload, 16), Word(payload, 20), payload[24..].ToArray());
            case 0x162:
                Require(payload, 24, message.Opcode);
                return new GoldMovementEndEvent(message, Word(payload, 0), Word(payload, 4), Word(payload, 8),
                    Word(payload, 12), Word(payload, 16), Word(payload, 20), payload[24..].ToArray());
            case 0x164:
                Require(payload, 4, message.Opcode);
                return new GoldIdleEvent(message, Word(payload, 0), payload[4..].ToArray());
            // 0x4C7881 / 0x4C7758: object ref is NOT a script trigger or Static.pak ID.
            case 0x111:
            case 0x112:
                Require(payload, 4, message.Opcode);
                return new GoldDoorEvent(message, Word(payload, 0), message.Opcode == 0x112, payload[4..].ToArray());
            case 0x169:
            case 0x16B:
            case 0x16F:
                RequireObjectState(payload, 24, message.Opcode);
                return new GoldObjectEvent(message, Word(payload, 0), Word(payload, 4),
                    [Word(payload, 8), Word(payload, 12), Word(payload, 16), Word(payload, 20)], payload[24..].ToArray());
            case 0x16C:
            case 0x16E:
                RequireObjectState(payload, 8, message.Opcode);
                return new GoldObjectEvent(message, Word(payload, 0), Word(payload, 4), [], payload[8..].ToArray());
            default:
                return new GoldUnknownEvent(message);
        }
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static void RequireObjectState(ReadOnlySpan<byte> payload, int prefixSize, ushort opcode)
    {
        Require(payload, prefixSize + GoldObjectStateLayout.Size, opcode);
        _ = GoldObjectBaseState.FromPayload(payload[prefixSize..]);
    }
    private static void RequireExact(ReadOnlySpan<byte> bytes, int size, ushort opcode)
    {
        if (bytes.Length != size) throw new InvalidDataException($"Gold opcode 0x{opcode:X4} needs exactly {size} payload bytes.");
    }
    private static void Require(ReadOnlySpan<byte> bytes, int minimum, ushort opcode)
    {
        if (bytes.Length < minimum) throw new InvalidDataException($"Gold opcode 0x{opcode:X4} needs at least {minimum} payload bytes.");
    }
}
