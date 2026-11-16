using System.Buffers.Binary;

namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Arguments selected by the original physical file reader. This is
/// a pending request, not a restored animation or a loaded model.</summary>
public readonly record struct SacredSavedAnimationRequest(uint Selection, float FirstArgument, float SecondArgument)
{
    public bool RequiresActivation => Selection != 0;
    public static SacredSavedAnimationRequest Read(ReadOnlySpan<byte> physical)
    {
        if (physical.Length != SacredSavedPhysicalLayout.Size)
            throw new InvalidDataException("Animation import requires the complete saved physical prefix.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(physical[SacredSavedPhysicalLayout.AnimationSelectionOffset..]),
            (float)BinaryPrimitives.ReadDoubleLittleEndian(physical[SacredSavedPhysicalLayout.AnimationFirstArgumentDoubleOffset..]),
            (float)BinaryPrimitives.ReadDoubleLittleEndian(physical[SacredSavedPhysicalLayout.AnimationSecondArgumentDoubleOffset..]));
    }
}
