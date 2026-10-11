using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Recovered column-flare state, light binding and constructor texture handle.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20B8)]
public struct SacredColumnFlareFxObjectLayout
{
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x80), BinaryUnknown] public uint PendingCode;
    [FieldOffset(0x84), BinaryUnknown] public uint PendingValue;
    [FieldOffset(0xB4)] public uint LightHandle;
    [FieldOffset(0x20A0)] public SacredColumnFlareFxStateLayout State;
    [FieldOffset(0x20B4)] public uint TextureHandle;
}
