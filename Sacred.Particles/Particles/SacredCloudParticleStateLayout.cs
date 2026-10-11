using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 784070 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredCloudParticleStateLayout
{
    public const int SerializedSize = 0xA0;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly float EmissionDuration;
    [FieldOffset(0x008)] public readonly byte Active;
    [FieldOffset(0x009)] public readonly byte EmissionActive;
    [FieldOffset(0x00A), BinaryUnknown] public readonly ushort Unknown00A;
    [FieldOffset(0x00C)] public readonly uint ColorTopLeft;
    [FieldOffset(0x010)] public readonly uint ColorTopRight;
    [FieldOffset(0x014)] public readonly uint ColorBottomLeft;
    [FieldOffset(0x018)] public readonly uint ColorBottomRight;
    [FieldOffset(0x01C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x03C)] public readonly SacredParticleEmissionLayout Emission;
}
