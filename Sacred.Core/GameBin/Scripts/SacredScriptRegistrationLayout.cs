using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Opcode 40 embeds this entire 80-byte record after its four-byte header.
/// Gold 0x475680 appends it to the +0x755C vector; bank 4 section 0 copies that vector.
/// Unresolved integer fields retain their offsets until their individual meanings are established.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptRegistrationLayout
{
    public const ushort Opcode = 40;
    public const int SerializedSize = 80;
    [FieldOffset(0), BinaryUnknown] public readonly int Word0;
    [FieldOffset(4), BinaryString("Name", 64, "ISO-8859-1")] private readonly byte _name;
    /// <summary>Index into the 84-byte FunkStru table. Gold 0x476B07/0x476B28 indexes
    /// it before converting the referenced function to its multiplayer identity.</summary>
    [FieldOffset(68)] public readonly int FunctionIndex;
    [FieldOffset(72), BinaryUnknown] public readonly int Word72;
    [FieldOffset(76), BinaryUnknown] public readonly int Word76;
}
