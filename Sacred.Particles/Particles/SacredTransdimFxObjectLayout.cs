using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20CC)]
public struct SacredTransdimFxObjectLayout
{
    [FieldOffset(0x74)] public ushort ControlFlags;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x20A0)] public SacredTransdimFxStateLayout State;
    [FieldOffset(0x80), BinaryUnknown] public uint PendingCode;
    [FieldOffset(0x84)] public uint TextureCommand;
    [FieldOffset(0x88)] public uint CommandTextureHandle;
    [FieldOffset(0x8C)] public float CommandOffsetX;
    [FieldOffset(0x90)] public float CommandOffsetY;
    [FieldOffset(0x94)] public float CommandOffsetZ;
    [FieldOffset(0x20C4)] public uint FlareTextureHandle;
    [FieldOffset(0x20C8)] public uint CommandTextureBinding;
}
