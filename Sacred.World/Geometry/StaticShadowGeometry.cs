using System.Numerics;
using Sacred.Core.Pak.Items;

namespace Sacred.World.Geometry;

/// <summary>Resolves the file-authored static-shadow quad relative to a composed sprite.</summary>
public static class StaticShadowGeometry
{
    public const int AtlasColumns = 16;
    public const int AtlasRows = 16;

    public static StaticShadowQuad Create(
        short authoredAnchorX,
        short authoredAnchorY,
        ushort authoredExtent,
        SacredItemStaticShadowProjection projection)
    {
        var extent = (float)authoredExtent;
        return new StaticShadowQuad(
            new Vector2(
                authoredAnchorX * 2.0f,
                authoredAnchorY * 2.0f),
            extent,
            extent,
            projection == SacredItemStaticShadowProjection.Directional
                ? new Vector2(extent, -extent)
                : new Vector2(0.0f, -extent));
    }
}

public readonly record struct StaticShadowQuad(
    Vector2 RootOffset,
    float ContactExtent,
    float ProjectionExtent,
    Vector2 Projection);
