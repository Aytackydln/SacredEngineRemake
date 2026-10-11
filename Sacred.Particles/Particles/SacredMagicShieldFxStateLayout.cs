using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Magic Shield's twenty-four-byte saved payload at native object +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredMagicShieldFxStateLayout
{
    public const int SerializedSize = 0x18;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public byte Active;
    [FieldOffset(0x05)] public byte Unknown05;
    [FieldOffset(0x06)] public ushort Unknown06;
    [FieldOffset(0x08)] public float Segments;
    [FieldOffset(0x0C)] public float HitFlash;
    [FieldOffset(0x10)] public int Phase;
    [FieldOffset(0x14)] public float Lifetime;
}
