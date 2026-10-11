using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 0x774190 transfers 0x3C bytes from object +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredHealingParticleStateLayout
{
    public const int SerializedSize = 0x3C;
    /// <summary>Exactly zero causes a full-vector radial burst after movement, including zero-time repeats.</summary>
    [FieldOffset(0x00)] public readonly float ElapsedTime;
    [FieldOffset(0x04), BinaryUnknown] public readonly ushort Unknown04;
    [FieldOffset(0x06), BinaryUnknown] public readonly ushort Unknown06;
    [FieldOffset(0x08)] public readonly uint CornerColor0;
    [FieldOffset(0x0C)] public readonly uint CornerColor1;
    [FieldOffset(0x10)] public readonly uint CornerColor2;
    [FieldOffset(0x14)] public readonly uint CornerColor3;
    [FieldOffset(0x18)] public readonly byte Active;
    [FieldOffset(0x19), BinaryUnknown] public readonly byte Unknown19;
    [FieldOffset(0x1A), BinaryUnknown] public readonly ushort Unknown1A;
    [FieldOffset(0x1C)] public readonly SacredParticleMotionLayout Motion;
}
