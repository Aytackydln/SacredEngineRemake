using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Bubble Trap Explosion saves 0xA0 bytes from object +20A0, including unused standard parameters.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredBubbleTrapExplosionFxStateLayout
{
    public const int SerializedSize = 0xA0;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public float InnerRadius;
    [FieldOffset(0x08)] public byte Active;
    [FieldOffset(0x09), BinaryUnknown] public byte Unknown009;
    [FieldOffset(0x0A), BinaryUnknown] public ushort Unknown00A;
    [FieldOffset(0x0C)] public uint CornerColor0;
    [FieldOffset(0x10)] public uint CornerColor1;
    [FieldOffset(0x14)] public uint CornerColor2;
    [FieldOffset(0x18)] public uint CornerColor3;
    [FieldOffset(0x1C)] public SacredParticleMotionLayout UnusedMotion;
    [FieldOffset(0x3C)] public SacredParticleEmissionLayout UnusedEmission;
}
