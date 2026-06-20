namespace Sacred.Core.World.Sector;

public readonly record struct LiquidSurface(
    int LocalX,
    int LocalY,
    byte SurfaceType,
    byte StyleId,
    sbyte AlphaLeft,
    sbyte AlphaTop,
    sbyte AlphaRight,
    sbyte AlphaBottom)
{
    // Authored floor-overlay insertion depth used when splitting terrain below and above liquid.
    public byte FloorInsertionDepth => (byte)(SurfaceType & 0x0F);
}
