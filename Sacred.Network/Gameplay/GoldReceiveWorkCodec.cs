using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 7DDEAF, 7DFD76, 7DFE78 and shared object/event helper receipts.</summary>
public static class GoldReceiveWorkCodec
{
    public const ushort ExperienceOpcode = 0x127;
    public const ushort StartPositionOpcode = 0x132;
    public const ushort PartialFxOpcode = 0x14B;
    public const ushort ControlledObjectOpcode = 0x16A;
    public const ushort ReconcileOpcode = 0x197;
    public const ushort PairEffectOpcode = 0x199;
    public const ushort CreatureEventOpcode = 0x19B;
    public const int StartProfileSize = 0x230;
    public const int PartialFxMinimum = GoldObjectFxStateLayout.MinimumSize - GoldObjectStateLayout.Size;

    public static GoldReceiveWorkEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var minimum = message.Opcode switch
        {
            ExperienceOpcode => 8, StartPositionOpcode => StartProfileSize + 4,
            PartialFxOpcode => 8 + PartialFxMinimum, ControlledObjectOpcode => 24 + GoldObjectStateLayout.Size,
            ReconcileOpcode => 0, PairEffectOpcode => 16, CreatureEventOpcode => 128,
            _ => throw new InvalidDataException("Not a Gold receive work packet.")
        };
        if (message.Payload.Length < minimum || message.Opcode is PairEffectOpcode or CreatureEventOpcode && message.Payload.Length != minimum)
            throw new InvalidDataException("Invalid Gold receive work packet length.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        switch (message.Opcode)
        {
            case ExperienceOpcode: return new GoldExperienceAwardEvent(owned, Word(0), unchecked((int)Word(4)));
            case StartPositionOpcode: return new GoldStartPositionDataEvent(owned, owned.Payload.AsMemory(0, StartProfileSize),
                GoldPackedProfileBlock.Decode(owned.Payload.AsSpan(StartProfileSize)));
            case ReconcileOpcode: return new GoldReferenceReconciliationEvent(owned);
            case PairEffectOpcode: return new GoldCreaturePairEffectEvent(owned, Word(0), Word(4), Word(8), Word(12));
            case CreatureEventOpcode: return new GoldCreatureEventBlock(owned, Word(0), owned.Payload);
            case ControlledObjectOpcode:
                // 7E481E rewrites the native message to 169 before calling its existing reader.
                var snapshot = (GoldObjectEvent)GoldGameEventDecoder.Decode(owned with { Opcode = 0x169 });
                return new GoldControlledObjectEvent(owned, snapshot);
            default:
                var state = owned.Payload.AsMemory(8); ValidatePartialFx(state.Span);
                return new GoldFxPartialEvent(owned, Word(0), Word(4), state);
        }
    }

    private static void ValidatePartialFx(ReadOnlySpan<byte> state)
    {
        var marker = BinaryPrimitives.ReadUInt32LittleEndian(state);
        if (marker is not (GoldObjectFxStateLayout.Marker or 0xBAADC0DE) ||
            BinaryPrimitives.ReadUInt32LittleEndian(state[182..]) > (state.Length - PartialFxMinimum) / 4)
            throw new InvalidDataException("Truncated Gold partial FX reference vector or invalid marker.");
    }

    internal static GoldObjectFxState MergePartial(GoldFxPartialEvent entry, GoldObjectFxState previous)
    {
        // 449D20 does not read a base block for 14B. 5B71B1 reads the same FX layer
        // as full snapshots, including DC/E0/wearer, and accepts both native markers.
        var state = entry.State.Span; var result = new byte[state.Length + GoldObjectStateLayout.Size];
        previous.ToPayload().AsSpan(0, GoldObjectStateLayout.Size).CopyTo(result);
        state.CopyTo(result.AsSpan(GoldObjectStateLayout.Size));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(GoldObjectStateLayout.Size), GoldObjectFxStateLayout.Marker);
        // 5B751F resets the runtime float at +88.
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(87), 0);
        return GoldObjectFxState.FromPayload(result);
    }
}
