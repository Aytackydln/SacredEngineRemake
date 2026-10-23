using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.World.Regions;

/// <summary>Gold cRegion's 24-byte records, retained in world bank 1's region table.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredRegionStateLayout
{
    public const int SerializedSize = 24;
    public const int StatusOffset = 2;
    public const int RequiredStatusOffset = 3;
    public const int RegionFactorOffset = 12;
    public const int StoryFactorOffset = 16;
    public const int DynamicFactorOffset = 20;
    [FieldOffset(0)] public readonly ushort Index;
    /// <summary>Signed reputation. Negative is hostile; otherwise compared with RequiredStatus (Gold 0x61DA60).</summary>
    [FieldOffset(StatusOffset)] public readonly sbyte Status;
    [FieldOffset(RequiredStatusOffset)] public readonly sbyte RequiredStatus;
    [FieldOffset(4), BinaryUnknown] public readonly uint Word04;
    [FieldOffset(8)] public readonly uint Flags;
    [FieldOffset(RegionFactorOffset)] public readonly float RegionFactor;
    [FieldOffset(StoryFactorOffset)] public readonly float StoryFactor;
    [FieldOffset(DynamicFactorOffset)] public readonly float DynamicFactor;

    /// <summary>Gold 0x61DB50 multiplies these three factors in this order.</summary>
    public float MerchantFactor => (float)((double)DynamicFactor * StoryFactor * RegionFactor);
}
