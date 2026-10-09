using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Wall of Fire Line payload at +0x20A0; serializer 0x7AEE10.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredWallOfFireLineParticleStateLayout
{
    public const int SerializedSize = 0x490;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    /// <summary>Float from event +0x48. Births occur while the previous elapsed time is below it.</summary>
    [FieldOffset(0x004)] public readonly float EmissionDuration;
    [FieldOffset(0x008)] public readonly byte Active;
    [FieldOffset(0x009), BinaryUnknown] public readonly byte Unknown009;
    [FieldOffset(0x00A), BinaryUnknown] public readonly ushort Unknown00A;
    [FieldOffset(0x00C)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x40C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x42C)] public readonly SacredParticleEmissionLayout Emission;
}
