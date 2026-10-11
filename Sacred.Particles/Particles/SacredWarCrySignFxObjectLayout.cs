using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>War Cry Sign resources and six mutable FVF 0x1E2 vertices are outside its saved block.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x2188)]
public struct SacredWarCrySignFxObjectLayout
{
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x80), BinaryUnknown] public uint PendingCode;
    [FieldOffset(0x84)] public uint TextureCommand;
    [FieldOffset(0x20A0)] public SacredWarCrySignFxStateLayout State;
    [FieldOffset(0x20C0)] public uint StripTextureHandle;
    [FieldOffset(0x20C4)] public uint GroundTextureHandle;
    [FieldOffset(0x20C8)] public SacredFxSurfaceVertexLayout Vertex0;
    [FieldOffset(0x20E8)] public SacredFxSurfaceVertexLayout Vertex1;
    [FieldOffset(0x2108)] public SacredFxSurfaceVertexLayout Vertex2;
    [FieldOffset(0x2128)] public SacredFxSurfaceVertexLayout Vertex3;
    [FieldOffset(0x2148)] public SacredFxSurfaceVertexLayout Vertex4;
    [FieldOffset(0x2168)] public SacredFxSurfaceVertexLayout Vertex5;
}
