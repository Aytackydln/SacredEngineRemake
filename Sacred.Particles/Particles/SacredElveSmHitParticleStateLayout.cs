using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Elve SM Hit payload at +0x20A0; serializer 0x791260.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredElveSmHitParticleStateLayout
{
    public const int SerializedSize = 0x49C;
    [FieldOffset(0x000)] public readonly uint EventSubtype;
    [FieldOffset(0x004)] public readonly float ElapsedTime;
    [FieldOffset(0x008), BinaryUnknown] public readonly uint Unknown008;
    [FieldOffset(0x00C)] public readonly byte Active;
    [FieldOffset(0x00D), BinaryUnknown] public readonly byte Unknown00D;
    [FieldOffset(0x00E), BinaryUnknown] public readonly ushort Unknown00E;
    [FieldOffset(0x010)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x410)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x430)] public readonly SacredParticleEmissionLayout Emission;
    /// <summary>Special subtype's progress; its updater adds twice the frame duration.</summary>
    [FieldOffset(0x494)] public readonly float SpecialProgress;
    /// <summary>atan2(event direction) plus the native quarter turn.</summary>
    [FieldOffset(0x498)] public readonly float SpecialDirectionAngle;
}
