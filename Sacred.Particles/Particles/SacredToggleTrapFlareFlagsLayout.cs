using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Trap01 consumes bit 0x0008 at object +74 and writes notification kind at +7C.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x80)]
public struct SacredToggleTrapFlareFlagsLayout
{
    public const ushort ToggleMask = 0x0008;
    [FieldOffset(0x74)] public ushort Flags;
    [FieldOffset(0x7C)] public uint Notification;
}
