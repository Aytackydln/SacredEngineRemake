using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Fireball serializer 0x76B930 writes 0x494 bytes from +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredFireballParticleStateLayout
{
    public const int SerializedSize = 0x494;
    [FieldOffset(0)] public readonly float Age;
    [FieldOffset(4)] public readonly SacredParticleColorTableLayout Colors;
    /// <summary>Updated to (sin(2 * age) + 1) / 2 before movement.</summary>
    [FieldOffset(0x404)] public readonly float FlarePulse;
    [FieldOffset(0x408)] public readonly byte Active;
    [FieldOffset(0x409), BinaryUnknown] public readonly byte Unknown409;
    [FieldOffset(0x40A), BinaryUnknown] public readonly ushort Unknown40A;
    [FieldOffset(0x40C)] public readonly uint ReleaseState;
    [FieldOffset(0x410)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x430)] public readonly SacredParticleEmissionLayout Emission;
}
