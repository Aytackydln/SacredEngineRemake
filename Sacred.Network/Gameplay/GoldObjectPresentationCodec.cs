using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldObjectAnimationEvent(GoldMessage Message, uint ObjectReference, uint Animation,
    uint FirstArgumentBits, uint SecondArgumentBits) : GoldGameEvent(Message);
public sealed record GoldContainerCloseEvent(GoldMessage Message, uint ObjectReference) : GoldGameEvent(Message);

public static class GoldObjectPresentationCodec
{
    public const ushort AnimationOpcode = 0x170;
    public const ushort ContainerCloseOpcode = 0x18D;
    public const uint AnimationChangedFlag = 0x02000000;
    public static GoldGameEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            AnimationOpcode => 16, ContainerCloseOpcode => 4,
            _ => throw new InvalidDataException("Expected Gold object animation or container close.")
        };
        if (message.Payload.Length < size) throw new InvalidDataException("Truncated Gold object presentation command.");
        var copy = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload.AsSpan(offset));
        return message.Opcode == AnimationOpcode ? new GoldObjectAnimationEvent(copy, Word(0), Word(4), Word(8), Word(12)) :
            new GoldContainerCloseEvent(copy, Word(0));
    }
}
