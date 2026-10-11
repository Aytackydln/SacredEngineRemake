using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 76ACB0 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSoulcatcherParticleStateLayout
{
    public const int SerializedSize = 0x918;
    [FieldOffset(0x000)] public readonly byte Active;
    [FieldOffset(0x001), BinaryUnknown] public readonly ushort Unknown001;
    [FieldOffset(0x003), BinaryUnknown] public readonly byte Unknown003;
    [FieldOffset(0x004)] public readonly SacredParticleColorTableLayout PrimaryColors;
    [FieldOffset(0x404)] public readonly SacredParticleColorTableLayout SecondaryColors;
    [FieldOffset(0x804)] public readonly float ElapsedTime;
    [FieldOffset(0x808)] public readonly float EmissionDuration;
    [FieldOffset(0x80C)] public readonly SacredParticleMotionLayout PrimaryMotion;
    [FieldOffset(0x82C)] public readonly SacredParticleMotionLayout SecondaryMotion;
    [FieldOffset(0x84C)] public readonly SacredParticleEmissionLayout PrimaryEmission;
    [FieldOffset(0x8B0)] public readonly SacredParticleEmissionLayout SecondaryEmission;
    [FieldOffset(0x914)] public readonly float HitFlareFactor;
}
