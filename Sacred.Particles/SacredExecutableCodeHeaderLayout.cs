using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles;

/// <summary>Recovered fields in this Sacred Gold executable's 0x374-byte decoded
/// loader header. Header words after +0 are XORed with the preceding encoded word.
/// These offsets are specific to the verified executable loader, not a general PE format.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredExecutableCodeHeaderLayout
{
    public const int SerializedSize = 0x374;
    [FieldOffset(0)] public readonly uint HeaderXorSeed;
    [FieldOffset(0x18), BinaryUnknown] public readonly uint UnknownFlags;
    [FieldOffset(0x2C)] public readonly uint OriginalEntryPointAddress;
    [FieldOffset(0x30)] public readonly uint CodeAddress;
    [FieldOffset(0x34)] public readonly uint CodeByteLength;
    /// <summary>Initial key for code decoding. Later code words use the previous encoded word.</summary>
    [FieldOffset(0x38)] public readonly uint CodeXorSeed;
}
