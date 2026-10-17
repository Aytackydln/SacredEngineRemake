using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Gold file base/physical import followed by packet export when native +0x64 is zero.
/// This imports the physical layer only; creature/item values and later hero initialization are separate.</summary>
public static class GoldSavedPhysicalStateImporter
{
    public static GoldPhysicalState Import(ReadOnlySpan<byte> savedPhysical)
    {
        if (savedPhysical.Length != SacredSavedPhysicalLayout.Size)
            throw new InvalidDataException("Saved physical import requires exactly 247 bytes.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(savedPhysical[SacredSavedPhysicalLayout.Word64Offset..]) != 0)
            throw new NotSupportedException("Nonzero native +0x64 requires the unimplemented model/animation import path.");
        return ImportRetainedState(savedPhysical);
    }

    /// <summary>Project saved physical fields for upload, retaining animation selection and the saved bone vector.
    /// Does not create a model or advance animation; the nonzero +0x64 native activation path is deferred.</summary>
    public static GoldPhysicalState ImportRetainedState(ReadOnlySpan<byte> savedPhysical)
    {
        if (savedPhysical.Length != SacredSavedPhysicalLayout.Size)
            throw new InvalidDataException("Saved physical import requires exactly 247 bytes.");
        // TODO: Bind nonzero +0x64 to actor/model initialization when the incoming world is applied.
        var saved = MemoryMarshal.Read<SacredSavedObjectBaseLayout>(savedPhysical);
        var flags = saved.Flags & 0xFBFFEFFF; // 0x449AA0 file import.
        if ((flags & 2) == 0) flags |= 0x400000; // 0x44FC50 physical import.
        var wordB0 = BinaryPrimitives.ReadUInt32LittleEndian(savedPhysical[SacredSavedPhysicalLayout.WordB0Offset..]);
        var useCC = (flags & GoldPhysicalStateLayout.VectorCCFlag) != 0;
        wordB0 = useCC ? wordB0 & ~4u : wordB0 | 4u;
        if (useCC) flags &= ~0x200u;
        flags &= ~0x8000u;
        var basis = new GoldObjectBaseState(saved.ObjectReference, saved.Resource, flags,
            saved.Sector, saved.X, saved.Y, saved.Surface, saved.Word28, saved.Word34,
            saved.Byte38, saved.Owner, saved.Word3C);
        var payload = new byte[GoldPhysicalStateLayout.Size];
        basis.ToPayload().CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldPhysicalStateLayout.MarkerOffset), GoldPhysicalStateLayout.Marker);
        Copy(savedPhysical, payload, SacredSavedPhysicalLayout.Word64Offset, GoldPhysicalStateLayout.Word64Offset, 20);
        Copy(savedPhysical, payload, SacredSavedPhysicalLayout.Vector70Offset, GoldPhysicalStateLayout.Vector70Offset, 12);
        Copy(savedPhysical, payload, SacredSavedPhysicalLayout.VectorD8Offset, GoldPhysicalStateLayout.VectorD8Offset, 24);
        Copy(savedPhysical, payload, SacredSavedPhysicalLayout.Word94Offset, GoldPhysicalStateLayout.Word94Offset, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldPhysicalStateLayout.WordB0Offset), wordB0);
        Copy(savedPhysical, payload, useCC ? SacredSavedPhysicalLayout.VectorCCOffset : SacredSavedPhysicalLayout.BoneVectorOffset, GoldPhysicalStateLayout.SelectedVectorOffset, 12);
        return GoldPhysicalState.FromPayload(payload);
    }

    /// <summary>Apply the saved-object loader's reference/owner/flag adjustments at its 0x16E send boundary.</summary>
    public static GoldPhysicalState ImportParticipantObject(ReadOnlySpan<byte> savedPhysical, uint reference, byte playerSlot)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        if (reference is < GoldReferenceCodec.FirstObjectReference or >= GoldReferenceCodec.ReferenceLimit)
            throw new ArgumentOutOfRangeException(nameof(reference));
        var physical = Import(savedPhysical);
        var flags = physical.BaseState.Flags & ~0x200010u;
        if ((flags & 2) != 0) flags &= ~0x30u;
        var basis = physical.BaseState with { ObjectReference = reference, Owner = playerSlot, Flags = flags };
        var payload = physical.ToPayload();
        basis.ToPayload().CopyTo(payload, 0);
        return GoldPhysicalState.FromPayload(payload);
    }

    private static void Copy(ReadOnlySpan<byte> source, Span<byte> target, int sourceOffset, int targetOffset, int length)
        => source.Slice(sourceOffset, length).CopyTo(target[targetOffset..]);
}
