using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Door Spike native object resources and inputs, outside its 0xB0-byte saved block.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x215C)]
public struct SacredDoorSpikeFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    /// <summary>Mask 0x08 is consumed when phase 1 begins shrinking; all other bits are preserved.</summary>
    [FieldOffset(0x74)] public ushort ControlFlags;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x80), BinaryUnknown] public uint Unknown080;
    [FieldOffset(0x98)] public uint RenderRadius;
    [FieldOffset(0xA4)] public float GroundHeightOffset;
    [FieldOffset(0x20A0)] public SacredDoorSpikeFxStateLayout State;
    [FieldOffset(0x2150)] public uint ModelHandleLow;
    [FieldOffset(0x2154)] public uint ModelHandleHigh;
    [FieldOffset(0x2158)] public uint SurfaceTextureHandle;
}
