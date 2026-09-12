using Sacred.Core.World.Sector;

namespace Sacred.Engine.Rendering;

/// <summary>Applies Sacred's static-object surface layers to the current indoor building.</summary>
internal static class TerrainStaticSurfaceVisibility
{
    private const int ExteriorActiveLayer = 1;

    public static bool TryResolve(
        StaticWorldObject staticObject,
        IndoorTileGroup? activeIndoorGroup,
        out bool isIndoorSurface)
    {
        isIndoorSurface = false;
        if (activeIndoorGroup is null)
            return staticObject.SurfaceRenderLayer <= ExteriorActiveLayer;

        // Resolve membership at the parent building anchor. Sprite positions
        // can lie in empty border cells or overlap a neighboring building grid.
        var belongsToActiveSection = staticObject.IndoorAnchor is { } anchor &&
            activeIndoorGroup.TryGetAuthoredLocalTile(anchor.X, anchor.Y, out _, out _);
        if (staticObject.SurfaceRenderLayer > ExteriorActiveLayer)
        {
            isIndoorSurface = belongsToActiveSection &&
                              staticObject.SurfaceRenderLayer == activeIndoorGroup.SurfaceRenderLayer;
            return isIndoorSurface;
        }

        return !belongsToActiveSection ||
               staticObject.SurfaceRenderLayer != ExteriorActiveLayer ||
               !staticObject.UsesAlternateSurface;
    }

    /// <summary>
    /// Indicates that the sprite cannot be baked into a sector texture because
    /// entering an authored indoor grid can change whether it is visible.
    /// </summary>
    public static bool RequiresDynamicSurface(
        StaticWorldObject staticObject) =>
        staticObject.SurfaceRenderLayer > ExteriorActiveLayer ||
        staticObject.UsesAlternateSurface;

    /// <summary>Only invariant exterior objects may be baked into permanent sector textures.</summary>
    public static bool ShouldBakeExterior(StaticWorldObject staticObject) =>
        !RequiresDynamicSurface(staticObject);
}
