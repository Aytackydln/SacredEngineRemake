using System.Runtime.InteropServices;

namespace Sacred.Core.GameRes;

/// <summary>Header of a localized Sacred <c>global.res</c> string table.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct GameResourceHeaderLayout
{
    /// <summary>Serialized header size before index records.</summary>
    public const int SerializedSize = 0x04;

    /// <summary>Number of resource index records.</summary>
    [FieldOffset(0x00)]
    public readonly uint StringCount;
}

/// <summary>Index record pointing to one UTF-16 string in a Sacred resource file.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct GameResourceIndexLayout
{
    /// <summary>Serialized size of one string index record.</summary>
    public const int SerializedSize = 0x10;

    /// <summary>Stable numeric resource identifier.</summary>
    [FieldOffset(0x00)]
    public readonly uint ResourceId;

    /// <summary>Serialized offset used to locate the string data.</summary>
    [FieldOffset(0x04)]
    public readonly uint StringOffset;

    /// <summary>Unresolved index value at byte offset 0x08.</summary>
    [FieldOffset(0x08)]
    public readonly uint Unknown08;

    /// <summary>UTF-16 string length in bytes.</summary>
    [FieldOffset(0x0C)]
    public readonly uint ByteLength;
}
