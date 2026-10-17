using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Versioned DefPos.bin word immediately after its counted 100-byte position records.
/// Gold 0x46FE71..0x46FF02 reads this count before the 80-byte dialog table.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptDialogTableHeaderLayout
{
    public const int SerializedSize = 4;
    [FieldOffset(0)] public readonly uint DialogCount;
}
