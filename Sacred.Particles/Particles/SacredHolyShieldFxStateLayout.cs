using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Holy Shield's twenty-byte saved payload at native object +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredHolyShieldFxStateLayout
{
    public const int SerializedSize = 0x14;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public float RotationPhase;
    [FieldOffset(0x08)] public byte Active;
    [FieldOffset(0x09)] public byte Unknown09;
    [FieldOffset(0x0A)] public ushort Unknown0A;
    [FieldOffset(0x0C)] public int HitBrightness;
    [FieldOffset(0x10)] public float Lifetime;
}
