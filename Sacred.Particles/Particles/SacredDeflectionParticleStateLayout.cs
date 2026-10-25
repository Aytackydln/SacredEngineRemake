using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 76C740 serializes 16 bytes starting at deflection FX object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDeflectionParticleStateLayout
{
    public const int SerializedSize = 0x10;
    [FieldOffset(0)] public readonly int ParticleCount;
    [FieldOffset(4)] public readonly int StreakCount;
    [FieldOffset(8)] public readonly float ElapsedTime;
    [FieldOffset(12)] public readonly byte Active;
    [FieldOffset(13), BinaryUnknown] public readonly byte Unknown0D;
    [FieldOffset(14), BinaryUnknown] public readonly ushort Unknown0E;
}
