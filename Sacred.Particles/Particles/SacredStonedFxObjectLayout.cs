using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20C4)]
public struct SacredStonedFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    [FieldOffset(0x68)] public uint ParticlesBegin;
    [FieldOffset(0x6C)] public uint ParticlesEnd;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x80)] public uint OwnerTextureOperation;
    [FieldOffset(0x84)] public uint OwnerTextureMask;
    [FieldOffset(0x88)] public uint OwnerTextureHandle;
    [FieldOffset(0xA9)] public byte UseDefaultDuration;
    [FieldOffset(0x20A0)] public SacredStonedFxStateLayout State;
    [FieldOffset(0x20B4)] public uint ModelHandleLow;
    [FieldOffset(0x20B8)] public uint ModelHandleHigh;
    [FieldOffset(0x20BC)] public uint OwnerStoneTextureHandle;
    [FieldOffset(0x20C0)] public uint AuraTextureHandle;
}
