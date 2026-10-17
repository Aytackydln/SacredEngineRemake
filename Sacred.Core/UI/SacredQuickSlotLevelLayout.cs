using System.Runtime.InteropServices;

namespace Sacred.Core.UI;

/// <summary>Original executable taskbar unlock record, read backwards by the native level lookup.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly struct SacredQuickSlotLevelLayout
{
    public readonly ushort MinimumLevel;
    public readonly ushort SlotCount;
}
