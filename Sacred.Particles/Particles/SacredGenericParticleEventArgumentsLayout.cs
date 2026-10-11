using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Generic initializer's mapped event extent. Gold 796E53 copies +38 to
/// four corner colours; 796E50 selects the branch from the low byte of +3C.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x40)]
public readonly struct SacredGenericParticleEventArgumentsLayout
{
    [FieldOffset(0)] public readonly SacredLayeredParticleEventArgumentsLayout Common;
    [FieldOffset(0x38)] public readonly uint CornerColor;
    [FieldOffset(0x3C)] public readonly byte Subtype;
    [FieldOffset(0x3D), BinaryUnknown] public readonly byte Unknown03D;
    [FieldOffset(0x3E), BinaryUnknown] public readonly ushort Unknown03E;
}
