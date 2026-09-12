using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Mixed;

/// <summary>Header shared by each mixed.pak sprite group payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct MixedPakGroupLayout
{
    public const int SerializedSize = 0x10;

    /// <summary>Number of sprite-piece records following this header.</summary>
    [FieldOffset(0x00)] public readonly uint PieceCount;
    /// <summary>Native x: horizontal placement origin in pixels.</summary>
    [FieldOffset(0x04)] public readonly short AnchorX;
    /// <summary>Native y: vertical placement origin in pixels.</summary>
    [FieldOffset(0x06)] public readonly short AnchorY;
    /// <summary>Native sx; its rendering interpretation is not established.</summary>
    [FieldOffset(0x08)] public readonly short ExtentX;
    /// <summary>Native sy; its rendering interpretation is not established.</summary>
    [FieldOffset(0x0A)] public readonly short ExtentY;
    [FieldOffset(0x0C)] public readonly uint Reserved;
}
