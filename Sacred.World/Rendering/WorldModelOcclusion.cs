using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;

namespace Sacred.World.Rendering;

/// <summary>Software equivalent of the engine's opaque static-sprite painter-depth mask.</summary>
internal static class WorldModelOcclusion
{
    public static async Task<WorldModelOcclusionMask> BuildAsync(WorldStaticSpriteProvider sprites, IReadOnlyList<Sector> sectors,
        Vector2 center, int width, int height, float zoom, IndoorTileGroup? activeIndoorGroup)
    {
        var depths = new long[width * height];
        Array.Fill(depths, long.MinValue);
        var surfaceDepths = new float[depths.Length];
        Array.Fill(surfaceDepths, float.NegativeInfinity);
        var centerIso = IsometricProjection.WorldToIso(center) + IsometricProjection.TileAnchorOffset;
        var objects = sectors.SelectMany(s => s.StaticObjects.Objects).ToList();
        objects.Sort((left, right) =>
        {
            var leftQueue = sprites.GetItem(left.TypeId) is { } leftItem ? WorldStaticDrawOrder.QueueIndex(leftItem.ModelDesc, left) : 3;
            var rightQueue = sprites.GetItem(right.TypeId) is { } rightItem ? WorldStaticDrawOrder.QueueIndex(rightItem.ModelDesc, right) : 3;
            var queue = leftQueue.CompareTo(rightQueue);
            return queue != 0 ? queue : WorldStaticDrawOrder.Compare(left, right);
        });
        foreach (var obj in objects)
        {
            if (obj.IsExcludedFromNormalRender ||
                !WorldObjectSurfaceVisibility.TryResolveStatic(obj, activeIndoorGroup, out _)) continue;
            var item = sprites.GetItem(obj.TypeId);
            if (item is null || obj.Flags.HasFlag(StaticObjectFlags.NightOnly) && item.Value.ModelDesc.StaticSpriteFrameCount <= 1) continue;
            var sprite = await sprites.LoadAsync(obj);
            if (sprite is null) continue;
            var queue = WorldStaticDrawOrder.QueueIndex(item.Value.ModelDesc, obj);
            var isFrontLayer = item.Value.ModelDesc.IsFrontLayer;
            var usesDepth = item.Value.ModelDesc.GraphicType.HasFlag(SacredItemGraphicType.UsesSpriteDepth);
            var left = width * .5f + (obj.ProjectedX + 47.8f - sprite.AnchorX - centerIso.X) * zoom;
            var top = height * .5f + (obj.ProjectedY - .3f - sprite.AnchorY - centerIso.Y) * zoom;
            var depth = WorldStaticDrawOrder.TileKey(obj.TileWorldX, obj.TileWorldY, obj.ChainDepth);
            for (var y = Math.Max(0, (int)MathF.Ceiling(top)); y < Math.Min(height, top + sprite.Height * zoom); y++)
            for (var x = Math.Max(0, (int)MathF.Ceiling(left)); x < Math.Min(width, left + sprite.Width * zoom); x++)
            {
                var sx = Math.Clamp((int)((x - left) / zoom), 0, sprite.Width - 1);
                var sy = Math.Clamp((int)((y - top) / zoom), 0, sprite.Height - 1);
                if (sprite.Rgba[(sy * sprite.Width + sx) * 4 + 3] < 128) continue;
                // Ordinary sprites occlude by submission order, independently
                // of every model vertex's physical depth. Depth-enabled planes
                // accumulate separately because ordinary sprites do not write Z.
                if (usesDepth)
                {
                    var isoY = centerIso.Y + (y + .5f - height * .5f) / zoom;
                    surfaceDepths[y * width + x] = MathF.Max(surfaceDepths[y * width + x],
                        WorldSpriteDepth.FromIsoY(isoY, obj.GeometricHeightLayer));
                }
                else if (isFrontLayer)
                    depths[y * width + x] = long.MaxValue;
                else
                    depths[y * width + x] = queue < 3 ? long.MinValue : depth;
            }
        }
        return new WorldModelOcclusionMask(depths, surfaceDepths);
    }
}

internal sealed record WorldModelOcclusionMask(long[] PainterOrder, float[] SurfaceDepths);
