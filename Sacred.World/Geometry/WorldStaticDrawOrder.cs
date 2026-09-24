using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;

namespace Sacred.World.Geometry;

/// <summary>Static sprite submission order, independent of the X+Y model depth buffer.</summary>
public static class WorldStaticDrawOrder
{
    public static int QueueIndex(SacredItemGraphicFlags flags) =>
        flags.HasFlag(SacredItemGraphicFlags.FrontLayer) ? 4 : 3;

    public static int Compare(StaticWorldObject left, StaticWorldObject right) =>
        Compare(left.TileWorldX, left.TileWorldY, left.ChainDepth, left.InsertionOrder,
            right.TileWorldX, right.TileWorldY, right.ChainDepth, right.InsertionOrder);

    public static int Compare(int leftX, int leftY, int leftChain, int leftInsertion,
        int rightX, int rightY, int rightChain, int rightInsertion)
    {
        // Native drawLine advances the WLDX index by -63: X increases while Y
        // decreases on each X+Y scanline. Do not sort these ties by increasing Y.
        var depth = (leftX + leftY).CompareTo(rightX + rightY);
        if (depth != 0) return depth;
        var x = leftX.CompareTo(rightX);
        if (x != 0) return x;
        var chain = leftChain.CompareTo(rightChain);
        return chain != 0 ? chain : leftInsertion.CompareTo(rightInsertion);
    }
}
