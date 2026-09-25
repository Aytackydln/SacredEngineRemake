using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;

namespace Sacred.Engine.Rendering;

/// <summary>
/// Promotes baked mini objects whose authored draw order cannot be represented by
/// the sector texture. Liquids and over-water scenery are drawn after that texture.
/// </summary>
internal static class TerrainEmbeddedMiniObjectVisibility
{
    private const float LiquidOffsetX = 2.0f;
    private const float LiquidOffsetY = 1.0f;
    private const float LiquidWidth = 96.0f;
    private const float LiquidHeight = 48.0f;
    private const float OccluderCellWidth = 256.0f;
    private const float OccluderCellHeight = 128.0f;

    public static int PromoteOccluded(
        List<TerrainStaticSprite> sprites,
        IReadOnlyList<Sector> sectors,
        ISet<uint> promotedObjectIds,
        HashSet<long> activeLiquidTiles,
        Dictionary<long, List<TerrainStaticSprite>> overWaterBuckets)
    {
        BuildOccluderLookups(sectors, sprites, activeLiquidTiles, overWaterBuckets);

        var promoted = 0;
        for (var index = 0; index < sprites.Count; index++)
        {
            var sprite = sprites[index];
            if (!sprite.IsEmbeddedInTerrain || !sprite.IsMiniObject)
                continue;

            if (promotedObjectIds.Contains(sprite.StaticObjectId))
            {
                sprites[index] = sprite with { IsEmbeddedInTerrain = false };
                continue;
            }

            var bounds = SpriteBounds.From(sprite);
            if (!IntersectsLiquid(bounds, activeLiquidTiles) &&
                !IntersectsEarlierOverWaterSprite(sprite, bounds, overWaterBuckets))
            {
                continue;
            }

            sprites[index] = sprite with { IsEmbeddedInTerrain = false };
            promotedObjectIds.Add(sprite.StaticObjectId);
            promoted++;
        }

        return promoted;
    }

    private static void BuildOccluderLookups(
        IReadOnlyList<Sector> sectors,
        IReadOnlyList<TerrainStaticSprite> sprites,
        HashSet<long> activeLiquidTiles,
        Dictionary<long, List<TerrainStaticSprite>> overWaterBuckets)
    {
        activeLiquidTiles.Clear();
        overWaterBuckets.Clear();

        foreach (var sector in sectors)
        {
            var originX = sector.Coord.X * Sector.TileCount;
            var originY = sector.Coord.Y * Sector.TileCount;
            foreach (var liquid in sector.LiquidSurfaces.Surfaces)
            {
                if (liquid.AlphaLeft >= 0 && liquid.AlphaTop >= 0 &&
                    liquid.AlphaRight >= 0 && liquid.AlphaBottom >= 0)
                {
                    continue;
                }

                activeLiquidTiles.Add(TileKey(
                    originX + liquid.LocalX,
                    originY + liquid.LocalY));
            }
        }

        foreach (var sprite in sprites)
        {
            if (!sprite.RendersOverWater)
                continue;

            var bounds = SpriteBounds.From(sprite);
            var minCellX = CellCoordinate(bounds.Left, OccluderCellWidth);
            var maxCellX = CellCoordinate(bounds.Right, OccluderCellWidth);
            var minCellY = CellCoordinate(bounds.Top, OccluderCellHeight);
            var maxCellY = CellCoordinate(bounds.Bottom, OccluderCellHeight);
            for (var cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                for (var cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    var key = TileKey(cellX, cellY);
                    if (!overWaterBuckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new List<TerrainStaticSprite>(4);
                        overWaterBuckets.Add(key, bucket);
                    }

                    bucket.Add(sprite);
                }
            }
        }
    }

    private static bool IntersectsLiquid(
        SpriteBounds sprite,
        IReadOnlySet<long> activeLiquidTiles)
    {
        if (activeLiquidTiles.Count == 0)
            return false;

        // A liquid tile can overlap the sprite only when its projected origin is
        // inside this rectangle. Convert its four corners back to world space,
        // then inspect only the small range of possible integer tile positions.
        var left = sprite.Left - LiquidOffsetX - LiquidWidth;
        var top = sprite.Top - LiquidOffsetY - LiquidHeight;
        var right = sprite.Right - LiquidOffsetX;
        var bottom = sprite.Bottom - LiquidOffsetY;
        var topLeft = IsometricProjection.IsoToWorld(new Vector2(left, top));
        var topRight = IsometricProjection.IsoToWorld(new Vector2(right, top));
        var bottomLeft = IsometricProjection.IsoToWorld(new Vector2(left, bottom));
        var bottomRight = IsometricProjection.IsoToWorld(new Vector2(right, bottom));
        var minX = (int)MathF.Floor(MathF.Min(
            MathF.Min(topLeft.X, topRight.X),
            MathF.Min(bottomLeft.X, bottomRight.X)));
        var maxX = (int)MathF.Ceiling(MathF.Max(
            MathF.Max(topLeft.X, topRight.X),
            MathF.Max(bottomLeft.X, bottomRight.X)));
        var minY = (int)MathF.Floor(MathF.Min(
            MathF.Min(topLeft.Y, topRight.Y),
            MathF.Min(bottomLeft.Y, bottomRight.Y)));
        var maxY = (int)MathF.Ceiling(MathF.Max(
            MathF.Max(topLeft.Y, topRight.Y),
            MathF.Max(bottomLeft.Y, bottomRight.Y)));

        for (var worldY = minY; worldY <= maxY; worldY++)
        {
            for (var worldX = minX; worldX <= maxX; worldX++)
            {
                if (!activeLiquidTiles.Contains(TileKey(worldX, worldY)))
                    continue;

                var iso = IsometricProjection.WorldToIso(worldX, worldY);
                if (IntersectsDiamond(
                        sprite,
                        iso.X + LiquidOffsetX,
                        iso.Y + LiquidOffsetY,
                        LiquidWidth,
                        LiquidHeight))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IntersectsEarlierOverWaterSprite(
        TerrainStaticSprite miniObject,
        SpriteBounds miniObjectBounds,
        IReadOnlyDictionary<long, List<TerrainStaticSprite>> overWaterBuckets)
    {
        var minCellX = CellCoordinate(miniObjectBounds.Left, OccluderCellWidth);
        var maxCellX = CellCoordinate(miniObjectBounds.Right, OccluderCellWidth);
        var minCellY = CellCoordinate(miniObjectBounds.Top, OccluderCellHeight);
        var maxCellY = CellCoordinate(miniObjectBounds.Bottom, OccluderCellHeight);
        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                if (!overWaterBuckets.TryGetValue(TileKey(cellX, cellY), out var bucket))
                    continue;

                foreach (var occluder in bucket)
                {
                    if (occluder.StaticObjectId == miniObject.StaticObjectId)
                        continue;
                    if (ComparePainterOrder(occluder, miniObject) >= 0)
                        continue;
                    if (miniObjectBounds.Intersects(SpriteBounds.From(occluder)))
                        return true;
                }
            }
        }

        return false;
    }

    private static int CellCoordinate(float value, float cellSize) =>
        (int)MathF.Floor(value / cellSize);

    private static long TileKey(int worldX, int worldY) =>
        ((long)worldX << 32) | (uint)worldY;

    private static int ComparePainterOrder(
        TerrainStaticSprite left,
        TerrainStaticSprite right)
    {
        var postModel = left.RequiresPostModelPass.CompareTo(right.RequiresPostModelPass);
        if (postModel != 0)
            return postModel;
        var queue = left.QueueIndex.CompareTo(right.QueueIndex);
        if (queue != 0)
            return queue;
        return WorldStaticDrawOrder.Compare(
            left.TileWorldX, left.TileWorldY, left.ChainDepth, left.InsertionOrder,
            right.TileWorldX, right.TileWorldY, right.ChainDepth, right.InsertionOrder);
    }

    private static bool IntersectsDiamond(
        SpriteBounds sprite,
        float left,
        float top,
        float width,
        float height)
    {
        if (!sprite.Intersects(new SpriteBounds(left, top, left + width, top + height)))
            return false;

        var centerX = left + width * 0.5f;
        var centerY = top + height * 0.5f;
        var nearestX = Math.Clamp(centerX, sprite.Left, sprite.Right);
        var nearestY = Math.Clamp(centerY, sprite.Top, sprite.Bottom);
        return MathF.Abs(nearestX - centerX) / (width * 0.5f) +
               MathF.Abs(nearestY - centerY) / (height * 0.5f) <= 1.0f;
    }

    private readonly record struct SpriteBounds(float Left, float Top, float Right, float Bottom)
    {
        public static SpriteBounds From(TerrainStaticSprite sprite) => new(
            sprite.IsoX,
            sprite.IsoY,
            sprite.IsoX + sprite.RenderWidth,
            sprite.IsoY + sprite.RenderHeight);

        public bool Intersects(SpriteBounds other) =>
            Left < other.Right && Right > other.Left &&
            Top < other.Bottom && Bottom > other.Top;
    }
}
