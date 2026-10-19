using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldObjectFlightCodec
{
    public static GoldObjectFlightEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (GoldObjectFlightLayout.DropOpcode or GoldObjectFlightLayout.ForcedOpcode))
            throw new ArgumentOutOfRangeException(nameof(message));
        var bytes = message.Payload.AsSpan();
        if (bytes.Length != GoldObjectFlightLayout.Size)
            throw new InvalidDataException("Gold object flight requires exactly 48 bytes.");
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(offset));
        return new(message with { Payload = bytes.ToArray() }, Word(GoldObjectFlightLayout.ReferenceOffset),
            new(BinaryPrimitives.ReadInt32LittleEndian(bytes[GoldObjectFlightLayout.XOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(bytes[GoldObjectFlightLayout.YOffset..]),
                Word(GoldObjectFlightLayout.SurfaceOffset), Word(GoldObjectFlightLayout.SectorOffset)),
            GoldPhysicalVector.Read(bytes[GoldObjectFlightLayout.TranslationOffset..]),
            GoldPhysicalVector.Read(bytes[GoldObjectFlightLayout.RotationOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[GoldObjectFlightLayout.HeightOffset..]));
    }
}
