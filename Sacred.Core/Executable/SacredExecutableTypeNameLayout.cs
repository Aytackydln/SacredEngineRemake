using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Executable;

/// <summary>Gold TypeManager symbolic-name row. Native 0x43CF0A..0x43CF7A
/// iterates its executable table by 68 bytes and indexes by the stored type ID.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredExecutableTypeNameLayout
{
    public const int SerializedSize = 68;
    public const int NameLength = 64;
    [FieldOffset(0)] public readonly uint TypeId;
    [FieldOffset(4), BinaryString("Name", NameLength, "ISO-8859-1")] private readonly byte _name;
}
