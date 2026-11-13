using System.Buffers.Binary;
using System.Numerics;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared initial fixed Effect command's full stream. The caller
/// supplies its logical identity and native projected position. Candidate unused
/// fields are zero-canonicalized only for this executable-matched family, after
/// actual update/full-render invariance checks. No publication or allocation.</summary>
public static class WorldFixedEffectSerialization
{
    public static GoldObjectFxState Create(SacredFixedEffectDefinitions definitions,
        GoldObjectBaseState identity, Vector3 projectedPosition, SacredRandomSequence random)
    {
        if (identity.ObjectType != definitions.Resource || identity.ObjectReference <= 16 ||
            identity.Owner is < 1 or > 16 || !float.IsFinite(projectedPosition.X) ||
            !float.IsFinite(projectedPosition.Y) || !float.IsFinite(projectedPosition.Z))
            throw new NotSupportedException("Initial Effect requires its resolved original family, reserved identity and finite native projection.");
        // Native constructor initializes the first pair; actual Start replaces it.
        random.Next(); random.Next();
        var first = (uint)random.Next() & definitions.SelectionMask;
        var second = (uint)random.Next() & definitions.SelectionMask;
        const int derivedSize = 0x34, baseSize = GoldParticleBaseStateLayout.Size;
        var body = new byte[GoldObjectFxStateLayout.MinimumSize + 12 + derivedSize + baseSize];
        (identity with { Flags = identity.Flags & ~0x10002u }).ToPayload().CopyTo(body, 0);
        Word(GoldObjectFxStateLayout.MarkerOffset, GoldObjectFxStateLayout.Marker);
        Word(GoldObjectFxStateLayout.FxTypeOffset, definitions.EventCode);
        // Actual network-owned static event: active system, live, send owner,
        // and no periodic update publication. These are separate from node activity.
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(GoldObjectFxStateLayout.FlagsOffset), 0xC3);
        Word(GoldObjectFxStateLayout.WordDcOffset, definitions.RechargeBits);
        Word(GoldObjectFxStateLayout.WordE0Offset, definitions.WordE0Bits);
        var tail = GoldObjectFxStateLayout.MinimumSize;
        Word(tail, GoldParticleSnapshotLayout.DerivedMarker); Word(tail + 4, derivedSize);
        var state = tail + GoldParticleSnapshotLayout.DerivedDataOffset;
        Word(state + 0x10, definitions.AngleBits); Word(state + 0x14, definitions.AngleBits);
        body[state + 0x20] = 1;
        Word(state + 0x24, first); Word(state + 0x28, second); Word(state + 0x2C, definitions.SelectionMaximum);
        var end = state + derivedSize; Word(end, GoldParticleSnapshotLayout.DerivedEndMarker);
        var particleBase = end + 4;
        Word(particleBase, GoldParticleSnapshotLayout.BaseMarker);
        Word(particleBase + 4, identity.ObjectType);
        Float(particleBase + 8, projectedPosition.X); Float(particleBase + 12, projectedPosition.Y); Float(particleBase + 16, projectedPosition.Z);
        Word(particleBase + GoldParticleBaseStateLayout.CapacityOffset, definitions.ParticleCapacity);
        body[particleBase + GoldParticleBaseStateLayout.NativeA8ByteOffset] = 1;
        Word(particleBase + GoldParticleBaseStateLayout.ObjectReferenceOffset, identity.ObjectReference);
        return GoldObjectFxState.FromPayload(body);
        void Word(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(offset), value);
        void Float(int offset, float value) => Word(offset, BitConverter.SingleToUInt32Bits(value));
    }
}
