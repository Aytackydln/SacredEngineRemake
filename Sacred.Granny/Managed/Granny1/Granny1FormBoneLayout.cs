using System.Runtime.InteropServices;

namespace Sacred.Granny.Managed.Granny1;

/// <summary>
/// Confirmed prefix of a 0xCA5E0C0A form-bone record in a Granny 1 model payload.
/// A form mesh with one binding and no vertex weights follows this skeleton slot.
/// The slot is not inferred from mesh order. Remaining record bytes are not mapped here.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
internal readonly struct Granny1FormBoneLayout
{
    public const int BoneIndexOffset = 0;
    public const int PrefixSize = 4;

    [FieldOffset(BoneIndexOffset)] public readonly uint SkeletonBoneIndex;
}
