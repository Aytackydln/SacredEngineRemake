using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 77E480 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredFreezeImpactParticleStateLayout
{
    public const int SerializedSize = 0x494;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly ushort Unknown005;
    [FieldOffset(0x007), BinaryUnknown] public readonly byte Unknown007;
    [FieldOffset(0x008)] public readonly float GlowFactor;
    [FieldOffset(0x00C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x02C)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x090)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x490)] public readonly byte ChainPending;
    [FieldOffset(0x491)] public readonly byte RemainingChainCount;
    [FieldOffset(0x492), BinaryUnknown] public readonly ushort Unknown492;
}
