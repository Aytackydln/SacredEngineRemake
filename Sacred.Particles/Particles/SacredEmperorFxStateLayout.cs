using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 0x7BDDA0 saves 0x494 bytes beginning at object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredEmperorFxStateLayout
{
    public const int SerializedSize = 0x494;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public byte Active;
    [FieldOffset(0x05), BinaryUnknown] public byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(0x08)] public float EmissionEnd;
    [FieldOffset(0x0C)] public float FlareSizeMultiplier;
    [FieldOffset(0x10)] public SacredParticleMotionLayout Motion;
    [FieldOffset(0x30)] public SacredParticleEmissionLayout Emission;
    [FieldOffset(0x94)] public SacredParticleColorTableLayout Colors;
}
