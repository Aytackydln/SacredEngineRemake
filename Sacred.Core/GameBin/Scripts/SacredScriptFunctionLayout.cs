using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>First table in Vectoren.bin: UInt32 count followed by native FunkStru records.
/// Gold's function dispatcher (0x46BA90) indexes these records with an 84-byte stride.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptFunctionLayout
{
    public const int SerializedSize = 84;
    [FieldOffset(0), BinaryString("Name", 64, "ISO-8859-1")] private readonly byte _name;
    /// <summary>Byte offset in the adjacent FunkCode.bin.</summary>
    [FieldOffset(64)] public readonly int Start;
    /// <summary>Byte length of this function, not an instruction count.</summary>
    [FieldOffset(68)] public readonly int ByteLength;
    [FieldOffset(72)] public readonly int Quest;
    [FieldOffset(76)] public readonly int Number;
    [FieldOffset(80)] public readonly uint Deleted;
}
