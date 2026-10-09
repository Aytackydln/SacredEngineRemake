using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Shaddar Magic payload at +0x20A0; serializer 0x7AAE00.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredShaddarMagicParticleStateLayout
{
    public const int SerializedSize = 0x494;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly float EmissionDuration;
    /// <summary>Zero emits; one drains. Native hit flag can also switch this state after an impact burst.</summary>
    [FieldOffset(0x008)] public readonly uint ReleaseState;
    [FieldOffset(0x00C)] public readonly byte Active;
    [FieldOffset(0x00D), BinaryUnknown] public readonly byte Unknown00D;
    [FieldOffset(0x00E), BinaryUnknown] public readonly ushort Unknown00E;
    [FieldOffset(0x010)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x410)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x430)] public readonly SacredParticleEmissionLayout Emission;
}
