using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 78FB70 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredGladiatorCastParticleStateLayout
{
    public const int SerializedSize = 0x4B4;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly float SimulationStart;
    [FieldOffset(0x008)] public readonly float EmissionEnd;
    [FieldOffset(0x00C)] public readonly byte Active;
    [FieldOffset(0x00D), BinaryUnknown] public readonly ushort Unknown00D;
    [FieldOffset(0x00F), BinaryUnknown] public readonly byte Unknown00F;
    [FieldOffset(0x010)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x410)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x430)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x494)] public readonly float RingFactor0;
    [FieldOffset(0x498)] public readonly float RingFactor1;
    [FieldOffset(0x49C)] public readonly float RingFactor2;
    [FieldOffset(0x4A0)] public readonly float RingFactor3;
    [FieldOffset(0x4A4)] public readonly float RingFactor4;
    [FieldOffset(0x4A8)] public readonly ushort CombatArtType;
    [FieldOffset(0x4AA), BinaryUnknown] public readonly ushort Unknown4AA;
    [FieldOffset(0x4AC)] public readonly float StrengthFactor;
    [FieldOffset(0x4B0)] public readonly byte BurstReleased;
    [FieldOffset(0x4B1), BinaryUnknown] public readonly ushort Unknown4B1;
    [FieldOffset(0x4B3), BinaryUnknown] public readonly byte Unknown4B3;
}
