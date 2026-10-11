using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x253C)]
public struct SacredMineSignFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    [FieldOffset(0x68)] public uint ParticlesBegin;
    [FieldOffset(0x6C)] public uint ParticlesEnd;
    /// <summary>Bit 0x08 requests one decrement; updater clears this bit after births.</summary>
    [FieldOffset(0x74)] public ushort Flags;
    [FieldOffset(0x7C)] public uint Notification;
    /// <summary>Raw argument forwarded to the mine-use audio helper at 0x6770E0.</summary>
    [FieldOffset(0xAC)] public uint AudioArgument;
    [FieldOffset(0x20A0)] public SacredMineSignFxStateLayout State;
    [FieldOffset(0x2538)] public uint ParticleTextureHandle;
}
