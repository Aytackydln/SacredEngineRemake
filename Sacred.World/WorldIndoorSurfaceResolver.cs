using Sacred.Assets.World.Static;
using Sacred.Core.World;
using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>Reads the native indoor parent/content chain used by getHeightWithLayer.</summary>
internal static class WorldIndoorSurfaceResolver
{
    public static (byte Level, float Height, (int X, int Y)? Anchor)? ResolveFloor(
        StaticPakArchive archive, IReadOnlyList<StaticWorldObject> objects,
        WldxIndoorGroupPayload grid, int heightGridIndex)
    {
        foreach (var obj in objects)
        {
            if (!obj.Flags.HasFlag(StaticObjectFlags.IndoorSurfaceParent)) continue;
            var anchor = obj.IndoorAnchor ?? (obj.TileWorldX, obj.TileWorldY);
            if (anchor.Item1 < grid.WorldX || anchor.Item1 >= grid.WorldX + grid.Width ||
                anchor.Item2 < grid.WorldY || anchor.Item2 >= grid.WorldY + grid.Height ||
                archive.Get(obj.StaticId) is not { } parent) continue;
            var seen = new HashSet<uint>();
            var id = parent.IndoorSurfaceChainHeadId;
            for (byte level = 1; id != 0 && level < 32 && seen.Add(id); level++)
            {
                if (archive.Get(id) is not { } child) break;
                if (child.HeightLevelId == heightGridIndex)
                    return (level, child.GeometricHeightLayer * 28.0f, anchor);
                id = child.NextStaticId;
            }
        }
        return null;
    }
}
