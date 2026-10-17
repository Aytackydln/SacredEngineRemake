using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x4CE750 uses ref/type followed by a partial creature stream.</summary>
public static class GoldCreatureUpdateCodec
{
    public static GoldGameEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = message.Payload.AsSpan();
        if (payload.Length < GoldCreatureUpdateLayout.PrefixSize + GoldCreatureUpdateLayout.MarkerSize ||
            !GoldObjectStateReader.HasMarker(payload, GoldCreatureUpdateLayout.PrefixSize, GoldCreatureStateLayout.Marker))
            throw new InvalidDataException("Truncated Gold creature update or unsupported marker.");
        var reference = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        var type = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        return message.Opcode switch
        {
            GoldCreatureUpdateLayout.StateOpcode => new GoldCreatureUpdateEvent(message, reference, type,
                GoldCreatureUpdateState.FromPayload(payload[GoldCreatureUpdateLayout.PrefixSize..])),
            GoldCreatureUpdateLayout.ValuesOpcode => new GoldCreatureValuesEvent(message, reference, type,
                ReadValues(payload[(GoldCreatureUpdateLayout.PrefixSize + GoldCreatureUpdateLayout.MarkerSize)..])),
            _ => throw new ArgumentOutOfRangeException(nameof(message), "Unsupported Gold creature update opcode.")
        };
    }

    public static GoldMessage CreateState(byte playerSlot, uint objectReference, uint objectType, GoldCreatureUpdateState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Create(GoldCreatureUpdateLayout.StateOpcode, playerSlot, objectReference, objectType, state.ToPayload());
    }

    public static GoldMessage CreateValues(byte playerSlot, uint objectReference, uint objectType, GoldCreatureValuesState values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.IsFull || !values.RemainingData.IsEmpty)
            throw new ArgumentException("Gold 0x11B needs a complete compact values block.", nameof(values));
        var body = new byte[GoldCreatureUpdateLayout.MarkerSize + values.SerializedSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body, GoldCreatureStateLayout.Marker);
        values.ToPayload().CopyTo(body, GoldCreatureUpdateLayout.MarkerSize);
        return Create(GoldCreatureUpdateLayout.ValuesOpcode, playerSlot, objectReference, objectType, body);
    }

    private static GoldCreatureValuesState ReadValues(ReadOnlySpan<byte> payload)
    {
        var values = GoldCreatureValuesState.FromPayload(payload, GoldCreatureUpdateLayout.ValuesOpcode);
        if (!values.RemainingData.IsEmpty) throw new InvalidDataException("Trailing Gold 0x11B values bytes.");
        return values;
    }

    private static GoldMessage Create(ushort opcode, byte playerSlot, uint reference, uint type, byte[] body)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[GoldCreatureUpdateLayout.PrefixSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), type);
        body.CopyTo(payload, GoldCreatureUpdateLayout.PrefixSize);
        return new(opcode, playerSlot, playerSlot, 0, payload);
    }
}
