using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>One bin/wea.bin pool: this UInt32 count followed immediately by
/// Count UInt32 resource IDs. Exactly 256 pools occur in index order, with no
/// file header. Gold reset 0x4A9C70 and post-StartCode 0x470B11 read this layout.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptEquipmentPoolLayout
{
    public const int SerializedSize = sizeof(uint);
    public const int TypeIdSize = sizeof(uint);
    public const int FilePoolCount = 256;
    /// <summary>Opcode 114 accepts indices 0..254; file pool 255 still loads.</summary>
    public const int DeclarationIndexLimit = 255;
    public const int InterpreterOffset = 0x448;
    public const int RuntimeVectorHeaderSize = 12;
    [FieldOffset(0)] public readonly uint Count;
}
