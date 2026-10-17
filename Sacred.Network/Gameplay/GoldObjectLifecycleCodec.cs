using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Decode original removal and owner notifications; native routing/authority belongs to the server.</summary>
public static class GoldObjectLifecycleCodec
{
    public static GoldGameEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            GoldObjectLifecycleLayout.RemoveOpcode or GoldObjectLifecycleLayout.ImmediateRemoveOpcode => GoldObjectLifecycleLayout.RemoveSize,
            GoldObjectLifecycleLayout.OwnerOpcode => GoldObjectLifecycleLayout.OwnerSize,
            _ => throw new ArgumentException("Expected a Gold object lifecycle message.", nameof(message))
        };
        if (message.Payload.Length != size) throw new InvalidDataException($"Gold opcode 0x{message.Opcode:X4} requires {size} payload bytes.");
        var reference = BinaryPrimitives.ReadUInt32LittleEndian(message.Payload);
        return message.Opcode == GoldObjectLifecycleLayout.OwnerOpcode
            ? new GoldObjectOwnerEvent(message, reference, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(4)))
            : new GoldObjectRemovalEvent(message, reference);
    }
}
