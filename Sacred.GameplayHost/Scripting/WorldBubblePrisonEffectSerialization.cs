using System.Buffers.Binary;
using System.Numerics;
using Sacred.Assets.Executable;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared initial BUBBLEPRISON full FX stream, with
/// shared RNG and native projected position. Proven unused backing is canonicalized
/// to zero; decoding still preserves received backing. No reference allocation.</summary>
public static class WorldBubblePrisonEffectSerialization
{
    public static GoldObjectFxState Create(SacredBubblePrisonDefinitions definitions,
        GoldObjectBaseState identity, Vector3 projectedPosition, Sacred.Core.Runtime.SacredRandomSequence random)
    {
        if (identity.ObjectType != definitions.Resource || identity.ObjectReference <= 16 ||
            identity.Owner is < 1 or > 16 || !float.IsFinite(projectedPosition.X) ||
            !float.IsFinite(projectedPosition.Y) || !float.IsFinite(projectedPosition.Z))
            throw new NotSupportedException("Initial BUBBLEPRISON needs its original family, reserved identity and finite projection.");
        var rings = Sacred.Core.Runtime.SacredBubblePrisonInitialization.Create(definitions.Parameters, random);
        var body = new byte[GoldObjectFxStateLayout.MinimumSize + 8 +
            GoldBubblePrisonParticleLayout.DerivedSize + GoldParticleBaseStateLayout.Size];
        (identity with { Flags = identity.Flags & ~0x10002u }).ToPayload().CopyTo(body, 0);
        Word(GoldObjectFxStateLayout.MarkerOffset, GoldObjectFxStateLayout.Marker);
        Word(GoldObjectFxStateLayout.FxTypeOffset, definitions.Fx.EventCode);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(GoldObjectFxStateLayout.FlagsOffset), 0xC3);
        Word(GoldObjectFxStateLayout.WordDcOffset, definitions.Fx.RechargeBits);
        Word(GoldObjectFxStateLayout.WordE0Offset, definitions.Fx.WordE0Bits);
        var tail = GoldObjectFxStateLayout.MinimumSize;
        Word(tail, GoldBubblePrisonParticleLayout.DerivedSize);
        var state = tail + GoldParticleSnapshotLayout.SizeOnlyDataOffset;
        body[state + GoldBubblePrisonParticleLayout.ActiveOffset] = 1;
        for (var index = 0; index < GoldBubblePrisonParticleLayout.RingCount; index++)
        {
            Float(GoldBubblePrisonParticleLayout.AnglesOffset + index * 4, rings.Angles[index]);
            Float(GoldBubblePrisonParticleLayout.AngularVelocitiesOffset + index * 4, rings.AngularVelocities[index]);
            Float(GoldBubblePrisonParticleLayout.HeightsOffset + index * 4, rings.Heights[index]);
        }
        void Float(int offset, float value) => Word(state + offset, BitConverter.SingleToUInt32Bits(value));
        var end = state + GoldBubblePrisonParticleLayout.DerivedSize;
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
