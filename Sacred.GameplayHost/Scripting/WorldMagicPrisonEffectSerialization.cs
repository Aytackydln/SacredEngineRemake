using System.Buffers.Binary;
using System.Numerics;
using Sacred.Assets.Executable;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared initial MAGICPRISON full FX stream, with explicit detail
/// input and native projected position. Proven unused backing is canonicalized
/// to zero; decoding still preserves received backing. No allocation or RNG.</summary>
public static class WorldMagicPrisonEffectSerialization
{
    public static GoldObjectFxState Create(SacredMagicPrisonDefinitions definitions,
        GoldObjectBaseState identity, Vector3 projectedPosition, byte detailLevel)
    {
        if (identity.ObjectType != definitions.Resource || identity.ObjectReference <= 16 ||
            identity.Owner is < 1 or > 16 || !float.IsFinite(projectedPosition.X) ||
            !float.IsFinite(projectedPosition.Y) || !float.IsFinite(projectedPosition.Z))
            throw new NotSupportedException("Initial MAGICPRISON needs its original family, reserved identity and finite projection.");
        var rate = definitions.RateBits(detailLevel); // Reject unsupported inputs before generating state.
        var palette = definitions.CreatePalette();
        var body = new byte[GoldObjectFxStateLayout.MinimumSize + 8 +
            GoldMagicPrisonParticleLayout.DerivedSize + GoldParticleBaseStateLayout.Size];
        (identity with { Flags = identity.Flags & ~0x10002u }).ToPayload().CopyTo(body, 0);
        Word(GoldObjectFxStateLayout.MarkerOffset, GoldObjectFxStateLayout.Marker);
        Word(GoldObjectFxStateLayout.FxTypeOffset, definitions.Fx.EventCode);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(GoldObjectFxStateLayout.FlagsOffset), 0xC3);
        Word(GoldObjectFxStateLayout.WordDcOffset, definitions.Fx.RechargeBits);
        Word(GoldObjectFxStateLayout.WordE0Offset, definitions.Fx.WordE0Bits);
        var tail = GoldObjectFxStateLayout.MinimumSize;
        Word(tail, GoldMagicPrisonParticleLayout.DerivedSize);
        var state = tail + GoldParticleSnapshotLayout.SizeOnlyDataOffset;
        body[state + GoldMagicPrisonParticleLayout.Native20A4ByteOffset] = 1;
        for (var index = 0; index < palette.Length; index++)
            Word(state + GoldMagicPrisonParticleLayout.ColorTableOffset + index * 4, palette[index]);
        foreach (var (offset, value) in definitions.InitialWords) Word(state + offset, value);
        Word(state + GoldMagicPrisonParticleLayout.Native2520FloatOffset, rate);
        var end = state + GoldMagicPrisonParticleLayout.DerivedSize;
        Word(end, GoldParticleSnapshotLayout.DerivedEndMarker);
        var particleBase = end + 4;
        Word(particleBase, GoldParticleSnapshotLayout.BaseMarker);
        Word(particleBase + 4, identity.ObjectType);
        Word(particleBase + 8, BitConverter.SingleToUInt32Bits(projectedPosition.X));
        Word(particleBase + 12, BitConverter.SingleToUInt32Bits(projectedPosition.Y));
        Word(particleBase + 16, BitConverter.SingleToUInt32Bits(projectedPosition.Z));
        body[particleBase + GoldParticleBaseStateLayout.NativeA8ByteOffset] = 1;
        Word(particleBase + GoldParticleBaseStateLayout.ObjectReferenceOffset, identity.ObjectReference);
        return GoldObjectFxState.FromPayload(body);
        void Word(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(offset), value);
    }
}
