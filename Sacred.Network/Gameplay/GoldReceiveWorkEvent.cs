using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Receipts whose consumers need runtime state outside the replicated object stream.</summary>
public abstract record GoldReceiveWorkEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldExperienceAwardEvent(GoldMessage Message, uint ObjectReference, int Award)
    : GoldReceiveWorkEvent(Message)
{
    public uint NativeEventId => 0x213;
}
public sealed record GoldStartPositionDataEvent(GoldMessage Message, ReadOnlyMemory<byte> Profile,
    ReadOnlyMemory<byte> DecodedData) : GoldReceiveWorkEvent(Message);
public sealed record GoldReferenceReconciliationEvent(GoldMessage Message) : GoldReceiveWorkEvent(Message);
public sealed record GoldControlledObjectEvent(GoldMessage Message, GoldObjectEvent Snapshot) : GoldReceiveWorkEvent(Message);
public sealed record GoldFxPartialEvent(GoldMessage Message, uint ObjectReference, uint ObjectType,
    ReadOnlyMemory<byte> State) : GoldReceiveWorkEvent(Message);
public sealed record GoldCreatureEventBlock(GoldMessage Message, uint ObjectReference, ReadOnlyMemory<byte> Command)
    : GoldReceiveWorkEvent(Message)
{
    public uint CommandCode => BinaryPrimitives.ReadUInt32LittleEndian(Command.Span[8..]);
}
public sealed record GoldCreaturePairEffectEvent(GoldMessage Message, uint SourceReference, uint TargetReference,
    uint Word4D4, uint Word4D8) : GoldReceiveWorkEvent(Message);
