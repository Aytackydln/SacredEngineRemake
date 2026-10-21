using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x2104)]
public struct SacredTerrorSprinterFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    [FieldOffset(0x7C)] public uint Notification;
    /// <summary>Mode one sets command 6: apply combat-art stat modifiers.</summary>
    [FieldOffset(0x80)] public uint PendingCode;
    /// <summary>First command operand; zero suppresses the additional custom modifier event.</summary>
    [FieldOffset(0x84)] public uint TextureCommand;
    [FieldOffset(0x98)] public uint CreationFlags;
    [FieldOffset(0xB4)] public uint LightHandle;
    [FieldOffset(0xC0)] public SacredFxSurfaceVertexLayout RingVertex0;
    [FieldOffset(0xE0)] public SacredFxSurfaceVertexLayout RingVertex1;
    [FieldOffset(0x100)] public SacredFxSurfaceVertexLayout RingVertex2;
    [FieldOffset(0x120)] public SacredFxSurfaceVertexLayout RingVertex3;
    [FieldOffset(0x20A0)] public SacredTerrorSprinterFxStateLayout State;
    [FieldOffset(0x20B0)] public uint GroundTextureHandle;
    [FieldOffset(0x20B4)] public SacredTerrorSprinterTextureHandles SegmentTextures;
}
