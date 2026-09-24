using Sacred.Core.World.Sector;
using Sacred.World.Rendering;

namespace Sacred.Engine.Rendering;

/// <summary>Applies Sacred's static-object surface layers to the current indoor building.</summary>
internal static class TerrainStaticSurfaceVisibility
{
    private const int ExteriorActiveLayer = 1;

    public static bool TryResolve(
        StaticWorldObject staticObject,
        IndoorTileGroup? activeIndoorGroup,
        out bool isIndoorSurface)
        => WorldObjectSurfaceVisibility.TryResolveStatic(
            staticObject,
            activeIndoorGroup,
            out isIndoorSurface);

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
