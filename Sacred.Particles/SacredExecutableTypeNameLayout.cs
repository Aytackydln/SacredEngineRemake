using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles;

/// <summary>Native symbolic type catalogue row: ID first, then a 64-byte name.
/// Sacred.exe 0x43CEC4 returns the ID from table + index * 0x44.
/// The table index is NOT the type ID; identifiers have gaps.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredExecutableTypeNameLayout
{
    public const int SerializedSize = 0x44;
    public const int NameLength = 64;
    [FieldOffset(0)] public readonly uint TypeId;
    [FieldOffset(4), BinaryString("Name", NameLength, "ISO-8859-1")]
    private readonly byte _name;
}
