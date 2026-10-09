using System.Runtime.InteropServices;

namespace Sacred.Core.UI;

/// <summary>Native 68-byte font table record. Flag meanings remain unverified.</summary>
[StructLayout(LayoutKind.Explicit, Size = 68)]
public struct SacredExecutableUiFontDefinitionLayout
{
    [FieldOffset(0)] public ushort PixelHeight;
    [FieldOffset(2)] public byte Unknown02;
    [FieldOffset(3)] public byte Unknown03;
    [FieldOffset(4)] public SacredExecutableUiFontFaceName FaceName;
}
