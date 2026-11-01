using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Instruction prefix in FunkCode.bin, StartCode.bin and the sgf.bin cache.
/// Sacred.exe 0x45F220 reads this prefix, then ByteLength - 4 operand bytes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptCommandHeaderLayout
{
    public const int SerializedSize = 4;
    /// <summary>Gold 0x48BB40: parse a 348-byte creature definition and apply it through 0x461540.</summary>
    public const ushort SetCreatureStateOpcode = 0x0003;
    /// <summary>Gold 0x481D80: named creature, repeated type operands; publish items before installation.</summary>
    public const ushort EquipItemOpcode = 0x0007;
    public const ushort CreateObjectOpcode = 0x0008;

    [FieldOffset(0)] public readonly ushort Opcode;

    /// <summary>Total instruction length, INCLUDING this four-byte prefix.</summary>
    [FieldOffset(2)] public readonly ushort ByteLength;
}
