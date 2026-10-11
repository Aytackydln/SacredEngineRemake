using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Native Speed Up saved block, object +0x20A0, twenty bytes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredSpeedUpFxStateLayout
{
    public const int SerializedSize = 0x14;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public float FrameTime;
    [FieldOffset(0x08)] public float ElapsedRate;
    [FieldOffset(0x0C)] public byte Active;
    [FieldOffset(0x0D)] public byte Unknown0D;
    [FieldOffset(0x0E)] public ushort Unknown0E;
    [FieldOffset(0x10)] public int TextureFrame;
}
