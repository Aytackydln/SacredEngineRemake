using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>MagLite transfers 0x10 bytes from object +20A0; +20AC also populates object +9C.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x10)]
public struct SacredMagLiteFxStateLayout
{
    [FieldOffset(0)] public float ElapsedTime;
    [FieldOffset(4)] public byte Active;
    [FieldOffset(5), BinaryUnknown] public byte Unknown005;
    [FieldOffset(6), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(8)] public float Lifetime;
    [FieldOffset(12)] public int LightStrength;
}
