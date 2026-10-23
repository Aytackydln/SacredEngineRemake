using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldScriptSpatialEvent(GoldMessage Message) : GoldScriptWorldEvent(Message);
public sealed record GoldMapIconEvent(GoldMessage Message, GoldMapIconState Icon) : GoldScriptSpatialEvent(Message);
public sealed record GoldAttractorEvent(GoldMessage Message, GoldAttractorState Attractor) : GoldScriptSpatialEvent(Message);

public static class GoldScriptSpatialCodec
{
    public static GoldScriptSpatialEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Payload.Length < GoldScriptSpatialLayout.Size)
            throw new InvalidDataException("Gold spatial insertion requires a 24-byte record.");
        var copy = message with { Payload = message.Payload.ToArray() };
        var data = copy.Payload.AsSpan(0, GoldScriptSpatialLayout.Size);
        return message.Opcode switch
        {
            GoldScriptSpatialLayout.MapIconOpcode => new GoldMapIconEvent(copy, GoldMapIconState.FromPayload(data)),
            GoldScriptSpatialLayout.AttractorOpcode => new GoldAttractorEvent(copy, GoldAttractorState.FromPayload(data)),
            _ => throw new InvalidDataException("Expected Gold map-icon or attractor insertion.")
        };
    }

    public static GoldMessage CreateMapIcon(byte slot, GoldMapIconState icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        return Create(slot, GoldScriptSpatialLayout.MapIconOpcode, icon.ToPayload());
    }
    public static GoldMessage CreateAttractor(byte slot, GoldAttractorState attractor)
    {
        ArgumentNullException.ThrowIfNull(attractor);
        return Create(slot, GoldScriptSpatialLayout.AttractorOpcode, attractor.ToPayload());
    }
    private static GoldMessage Create(byte slot, ushort opcode, byte[] data)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        return new(opcode, slot, slot, 0, data);
    }
}
