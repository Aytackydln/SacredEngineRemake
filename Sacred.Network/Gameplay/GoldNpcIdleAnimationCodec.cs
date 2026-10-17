using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldNpcIdleAnimationEvent(GoldMessage Message, uint InitiatorReference, uint TargetReference) : GoldGameEvent(Message)
{
    /// <summary>Instances at packet arrival. Later snapshots cannot retroactively change a queued decision.</summary>
    public GoldNpcIdleAnimationReplicaContext? ReplicaContext { get; init; }
}

public sealed record GoldNpcIdleAnimationReplicaContext(GoldReplicatedObject? Initiator, GoldReplicatedObject? Target);

public static class GoldNpcIdleAnimationCodec
{
    public static GoldNpcIdleAnimationEvent Decode(GoldMessage message)
    {
        if (message.Opcode != GoldNpcIdleAnimationLayout.Opcode || message.Payload.Length != GoldNpcIdleAnimationLayout.Size)
            throw new InvalidDataException("Gold NPC idle animation requires two object-reference words.");
        var copy = message.Payload.ToArray();
        return new(message with { Payload = copy }, BinaryPrimitives.ReadUInt32LittleEndian(copy), BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(4)));
    }
}
