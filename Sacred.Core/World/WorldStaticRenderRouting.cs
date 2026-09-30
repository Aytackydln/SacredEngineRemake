using Sacred.Core.Pak.Items;

namespace Sacred.Core.World;

/// <summary>
/// Logical static-object routing verified in Demo drawLine and the matching Gold
/// branches. Callers still apply visibility, parent-trigger matching, exclusions
/// and batching; this does not classify dynamic objects or terrain overlays.
/// </summary>
public static class WorldStaticRenderRouting
{
    /// <summary>
    /// Selects the native sprite queue. Gold can batch eligible floor mini-objects
    /// separately before water; this returns their logical layer, not that batch.
    /// Neither descriptor.Layer nor instance.SurfaceRenderLayer is a queue index.
    /// </summary>
    public static WorldRenderLayer GetSpriteLayer(
        in ItemsPakEntryModelDescLayout descriptor, in StaticObjectRecord instance)
    {
        if (descriptor.IsFrontLayer)
            return WorldRenderLayer.Ceiling;

        if ((descriptor.GraphicType & SacredItemGraphicType.Shadowable) != 0)
            return (instance.Flags & StaticObjectFlags.RearLayerBackground) != 0 || instance.TriggerState == 1
                ? WorldRenderLayer.Floor
                : WorldRenderLayer.Floor2;

        return WorldRenderLayer.Objects;
    }

    /// <summary>
    /// Selects separate ground shadows versus inline upper-surface shadows.
    /// The caller must also honor the shadow quality setting. Separate shadows
    /// are enqueued before parent-trigger visibility checks in the native path;
    /// inline shadows belong to the visible object's entry.
    /// </summary>
    public static WorldStaticShadowPlacement GetShadowPlacement(
        in ItemsPakEntryModelDescLayout descriptor, in StaticObjectRecord instance)
    {
        if (!descriptor.CastsStaticShadow || instance.SuppressesStaticShadow ||
            (instance.Flags & StaticObjectFlags.NormalRenderExclusionMask) != 0)
            return WorldStaticShadowPlacement.None;

        if (instance.TriggerState == 1)
            return WorldStaticShadowPlacement.ShadowsQueue;

        return instance.TriggerState > 1 &&
               (instance.Flags & StaticObjectFlags.AlternateSurface) != 0 &&
               (instance.Flags & StaticObjectFlags.RearLayerBackground) == 0 &&
               GetSpriteLayer(descriptor, instance) == WorldRenderLayer.Objects
            ? WorldStaticShadowPlacement.BeforeObject
            : WorldStaticShadowPlacement.None;
    }
}
