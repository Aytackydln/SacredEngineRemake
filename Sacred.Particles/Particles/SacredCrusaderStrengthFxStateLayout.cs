using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Native Strength saved block, object +0x20A0, sixteen bytes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredCrusaderStrengthFxStateLayout
{
    public const int SerializedSize = 0x10;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public float FrameTime;
    [FieldOffset(0x08)] public byte Active;
    [FieldOffset(0x09)] public byte Unknown09;
    [FieldOffset(0x0A)] public ushort Unknown0A;
    [FieldOffset(0x0C)] public int TextureFrame;
}
