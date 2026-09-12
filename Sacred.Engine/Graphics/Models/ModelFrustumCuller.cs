using System;
using System.Numerics;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Rejects model work outside the orthographic camera before it reaches the command list.</summary>
internal static class ModelFrustumCuller
{
    // A directional shadow is deliberately allowed to originate just outside the viewport.
    // This keeps its silhouette from popping at screen edges while still rejecting distant props.
    private const float DirectionalShadowCasterPadding = PlanarShadowProjection.MaximumLength;

    public static bool IsVisible(SacredCamera camera, SceneModel model) =>
        IntersectsCamera(camera, model.VisualCenter, model.WorldBoundsRadius, 0.0f);

    public static bool MayCastVisibleShadow(SacredCamera camera, SceneModel model) =>
        IntersectsCamera(camera, model.VisualCenter, model.WorldBoundsRadius, DirectionalShadowCasterPadding);

    private static bool IntersectsCamera(
        SacredCamera camera,
        Vector3 worldCenter,
        float worldRadius,
        float padding)
    {
        var viewCenter = Vector3.Transform(worldCenter, camera.View);
        var radius = worldRadius + padding;
        var projection = camera.Projection;
        var clipCenter = Vector4.Transform(new Vector4(viewCenter, 1.0f), projection);

        // The camera projection is orthographic, so a sphere remains a sphere in view space.
        // Calculate its conservative clip-space radius without allocating planes per frame.
        var clipRadiusX = radius * MathF.Sqrt(
            projection.M11 * projection.M11 + projection.M21 * projection.M21 + projection.M31 * projection.M31);
        var clipRadiusY = radius * MathF.Sqrt(
            projection.M12 * projection.M12 + projection.M22 * projection.M22 + projection.M32 * projection.M32);
        var clipRadiusZ = radius * MathF.Sqrt(
            projection.M13 * projection.M13 + projection.M23 * projection.M23 + projection.M33 * projection.M33);

        return clipCenter.X + clipRadiusX >= -clipCenter.W &&
               clipCenter.X - clipRadiusX <= clipCenter.W &&
               clipCenter.Y + clipRadiusY >= -clipCenter.W &&
               clipCenter.Y - clipRadiusY <= clipCenter.W &&
               clipCenter.Z + clipRadiusZ >= 0.0f &&
               clipCenter.Z - clipRadiusZ <= clipCenter.W;
    }
}
