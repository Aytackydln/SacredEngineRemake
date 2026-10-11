using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20B4)]
public struct SacredZhadumFxObjectLayout
{
    [FieldOffset(0x74)] public ushort ControlFlags;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x20A0)] public SacredZhadumFxStateLayout State;
    [FieldOffset(0x20B0)] public uint TextureHandle;
}
