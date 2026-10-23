using System.Buffers.Binary;
using Sacred.Core.World.Regions;

namespace Sacred.Network.Gameplay;

internal static class GoldWorldRegionValueReducer
{
    public static GoldWorldRegionTable Apply(GoldWorldRegionTable table, GoldWorldRegionValueEvent update)
    {
        var data = table.ToPayload();
        var row = data.AsSpan(update.RegionIndex * GoldWorldRegionTableLayout.RecordSize, GoldWorldRegionTableLayout.RecordSize);
        // Receiver uses FILD on a zero-extended UInt32, then rounds to Single on the stack.
        var value = (float)update.RawValue;
        if (update.ValueKind == GoldWorldRegionValueLayout.Status)
        {
            if (update.Add)
            {
                // Gold narrows the truncated 64-bit sum to signed 16 bits BEFORE its range check.
                var status = unchecked((short)(long)((double)unchecked((sbyte)row[SacredRegionStateLayout.StatusOffset]) + value));
                row[SacredRegionStateLayout.StatusOffset] = unchecked((byte)(status is < -127 or > 127 ? 127 : status));
            }
            else
            {
                value = Math.Clamp(value, -127f, 127f);
                row[SacredRegionStateLayout.StatusOffset] = unchecked((byte)(int)value);
                // Only assignment updates the region factor. Zero produces native positive infinity.
                BinaryPrimitives.WriteSingleLittleEndian(row[SacredRegionStateLayout.RegionFactorOffset..], (float)(1d + 128d / value));
            }
        }
        else
        {
            var offset = update.ValueKind switch
            {
                GoldWorldRegionValueLayout.RegionFactor => SacredRegionStateLayout.RegionFactorOffset,
                GoldWorldRegionValueLayout.StoryFactor => SacredRegionStateLayout.StoryFactorOffset,
                GoldWorldRegionValueLayout.DynamicFactor => SacredRegionStateLayout.DynamicFactorOffset,
                _ => -1
            };
            if (offset >= 0)
            {
                var previous = BinaryPrimitives.ReadSingleLittleEndian(row[offset..]);
                BinaryPrimitives.WriteSingleLittleEndian(row[offset..], update.Add ? (float)((double)previous + value) : value);
            }
        }
        return GoldWorldRegionTable.FromPayload(data);
    }
}
