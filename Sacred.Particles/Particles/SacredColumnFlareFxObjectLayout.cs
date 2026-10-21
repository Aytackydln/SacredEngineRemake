using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Recovered column-flare state, light binding and constructor texture handle.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20B8)]
public struct SacredColumnFlareFxObjectLayout
{
    [FieldOffset(0x7C)] public uint Notification;
    /// <summary>One-shot owner command. Panzer sets 6: apply combat-art stat modifiers.</summary>
    [FieldOffset(0x80)] public uint PendingCode;
    /// <summary>Command's first operand, zero for Panzer's generic stat-only branch.</summary>
    [FieldOffset(0x84)] public uint PendingValue;
    [FieldOffset(0xB4)] public uint LightHandle;
    [FieldOffset(0x20A0)] public SacredColumnFlareFxStateLayout State;
    [FieldOffset(0x20B4)] public uint TextureHandle;
}
