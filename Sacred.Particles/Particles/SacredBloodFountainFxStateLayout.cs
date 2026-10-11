using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold +0x20A0, saved as 0x490 bytes by 0x77BBB0. Texture handle follows.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredBloodFountainFxStateLayout
{
    public const int SerializedSize = 0x490;
    [FieldOffset(0x000)] public float ElapsedTime;
    [FieldOffset(0x004)] public float JetStrength;
    [FieldOffset(0x008)] public byte Active;
    [FieldOffset(0x009), BinaryUnknown] public byte Unknown009;
    [FieldOffset(0x00A), BinaryUnknown] public ushort Unknown00A;
    [FieldOffset(0x00C)] public SacredParticleColorTableLayout Colors;
    [FieldOffset(0x40C)] public SacredParticleMotionLayout Motion;
    [FieldOffset(0x42C)] public SacredParticleEmissionLayout Emission;
}
