using System.Buffers.Binary;
using System.Numerics;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldAttachedFxEvent(GoldMessage Message, uint Resource, uint FxSubtype,
    uint WearerReference, uint Selector, Vector3 Vector) : GoldGameEvent(Message);

public static class GoldAttachedFxCodec
{
    public static GoldAttachedFxEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldAttachedFxLayout.Opcode || message.Payload.Length != GoldAttachedFxLayout.Size)
            throw new InvalidDataException("Gold attached-FX creation needs exactly 28 bytes.");
        var bytes = message.Payload.AsSpan();
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(offset));
        var offset = GoldAttachedFxLayout.VectorOffset;
        var vector = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(offset + 4)..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[(offset + 8)..]));
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new InvalidDataException("Attached-FX vector contains non-finite coordinates.");
        return new(message with { Payload = bytes.ToArray() }, Word(GoldAttachedFxLayout.ResourceOffset),
            Word(GoldAttachedFxLayout.FxSubtypeOffset), Word(GoldAttachedFxLayout.WearerOffset),
            Word(GoldAttachedFxLayout.SelectorOffset), vector);
    }
}
