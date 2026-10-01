namespace Sacred.World.Geometry;

/// <summary>Depth of the horizontal sprite plane selected by descriptor bit 0x0200.</summary>
public static class WorldSpriteDepth
{
    // Demo getZFromScreenY: -screenY * (2 / 1.92), then -height / sin(atan(.5)).
    // A 24-pixel tile diagonal is 25 native depth units. Static.pak +0x33
    // supplies height in 28-unit steps, independently of the painter queue.
    public static float FromIsoY(float isoY, byte heightLevel) =>
        isoY / 24f + heightLevel * (28f * MathF.Sqrt(5f) / 25f);
}
