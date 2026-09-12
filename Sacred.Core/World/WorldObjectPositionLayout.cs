using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>
/// Native <c>sObjectWorldPosition</c>, embedded in each <c>Static.pak</c>
/// record. The eleven-byte layout is packed; it is not an aligned runtime
/// <c>cWorldPosition</c> instance.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct WorldObjectPositionLayout
{
    public const int SerializedSize = 0x0B;

    /// <summary>Native map: owning sector identifier.</summary>
    [FieldOffset(0x00)] public readonly ushort SectorId;
    /// <summary>Native projected world X coordinate.</summary>
    [FieldOffset(0x02)] public readonly int X;
    /// <summary>Native projected world Y coordinate.</summary>
    [FieldOffset(0x06)] public readonly int Y;
    /// <summary>Native z: height level stored with the object position.</summary>
    [FieldOffset(0x0A)] public readonly byte ElevationTier;
}
