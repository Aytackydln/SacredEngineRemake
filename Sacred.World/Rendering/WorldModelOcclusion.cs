using System.Numerics;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;

namespace Sacred.World.Rendering;

/// <summary>Software equivalent of the engine's opaque static-sprite painter-depth mask.</summary>
internal static class WorldModelOcclusion
{
    public static async Task<float[]> BuildAsync(WorldStaticSpriteProvider sprites, IReadOnlyList<Sector> sectors,
        Vector2 center, int width, int height, float zoom, IndoorTileGroup? activeIndoorGroup)
    {
        var depths = new float[width * height];
        Array.Fill(depths, float.NegativeInfinity);
        var centerIso = IsometricProjection.WorldToIso(center) + IsometricProjection.TileAnchorOffset;
        var objects = sectors.SelectMany(s => s.StaticObjects.Objects).ToList();
        objects.Sort((left, right) =>
        {
            var leftQueue = WorldStaticDrawOrder.QueueIndex(sprites.GetItem(left.TypeId)?.ModelDesc.GraphicFlags ?? 0);
            var rightQueue = WorldStaticDrawOrder.QueueIndex(sprites.GetItem(right.TypeId)?.ModelDesc.GraphicFlags ?? 0);
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
            var left = width * .5f + (obj.ProjectedX + 47.8f - sprite.AnchorX - centerIso.X) * zoom;
            var top = height * .5f + (obj.ProjectedY - .3f - sprite.AnchorY - centerIso.Y) * zoom;
            var depth = WorldPainterDepth.FromTile(obj.TileWorldX, obj.TileWorldY, obj.ChainDepth);
            for (var y = Math.Max(0, (int)MathF.Ceiling(top)); y < Math.Min(height, top + sprite.Height * zoom); y++)
            for (var x = Math.Max(0, (int)MathF.Ceiling(left)); x < Math.Min(width, left + sprite.Width * zoom); x++)
            {
                var sx = Math.Clamp((int)((x - left) / zoom), 0, sprite.Width - 1);
                var sy = Math.Clamp((int)((y - top) / zoom), 0, sprite.Height - 1);
                if (sprite.Rgba[(sy * sprite.Width + sx) * 4 + 3] < 128) continue;
                // Always-pass sprite rendering leaves the last submitted pixel's
                // depth, even when it is smaller than the previous sprite's key.
                depths[y * width + x] = depth;
            }
        }
        return depths;
    }
}
