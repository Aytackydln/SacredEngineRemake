using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 778AB0 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredGroundPoisonParticleStateLayout
{
    public const int SerializedSize = 0x494;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly float Lifetime;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly byte Active;
    [FieldOffset(0x409), BinaryUnknown] public readonly ushort Unknown409;
    [FieldOffset(0x40B), BinaryUnknown] public readonly byte Unknown40B;
    [FieldOffset(0x40C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x42C)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x490)] public readonly uint PulsingCircleColor;
}
