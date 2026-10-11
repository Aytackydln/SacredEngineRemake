using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 77F8B0 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredBurningLightParticleStateLayout
{
    public const int SerializedSize = 0x498;
    [FieldOffset(0x000)] public readonly byte Active;
    [FieldOffset(0x001), BinaryUnknown] public readonly ushort Unknown001;
    [FieldOffset(0x003), BinaryUnknown] public readonly byte Unknown003;
    [FieldOffset(0x004)] public readonly float ElapsedTime;
    [FieldOffset(0x008)] public readonly float BurstCountdown;
    [FieldOffset(0x00C)] public readonly float ActorRadius;
    [FieldOffset(0x010)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x410)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x430)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x494)] public readonly float EmissionDuration;
}
