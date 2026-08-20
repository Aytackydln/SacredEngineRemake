using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Mixed;

/// <summary>Header shared by each mixed.pak sprite group payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct MixedPakGroupLayout
{
    public const int SerializedSize = 0x10;

    /// <summary>Number of sprite-piece records following this header.</summary>
    [FieldOffset(0x00)] public readonly uint PieceCount;
    /// <summary>Unresolved value at byte offset 0x04.</summary>
    [FieldOffset(0x04)] public readonly uint Unknown04;
    /// <summary>Unresolved value at byte offset 0x08.</summary>
    [FieldOffset(0x08)] public readonly uint Unknown08;
    /// <summary>Unresolved value at byte offset 0x0C.</summary>
    [FieldOffset(0x0C)] public readonly uint Unknown0C;
}
