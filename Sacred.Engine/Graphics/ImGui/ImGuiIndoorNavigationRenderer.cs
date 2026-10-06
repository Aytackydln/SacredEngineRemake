using System.Numerics;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.World;
using Sacred.World.Geometry;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Shows the active floor's navigation cells at their sampled actor height.</summary>
internal static class ImGuiIndoorNavigationRenderer
{
    public static void Draw(SacredCamera camera, SceneState scene, int width, int height)
    {
        if (!scene.Debug.IndoorNavigationVisible || scene.Indoor.ActiveGroup is not { } floor) return;
        var transform = IsometricProjection.CreateScreenTransform(camera.ViewCenter,
            camera.GetViewportZoom(height), width, height);
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        for (var y = 0; y < floor.Height; y++)
        for (var x = 0; x < floor.Width; x++)
        {
            var elevation = floor.Elevation?[x, y] ?? default;
            var top = Point(x, y, elevation.NorthWest);
            var right = Point(x + 1, y, elevation.NorthEast);
            var bottom = Point(x + 1, y + 1, elevation.SouthEast);
            var left = Point(x, y + 1, elevation.SouthWest);
            var center = (top + right + bottom + left) * .25f;
            if (center.X < -100 || center.X > width + 100 || center.Y < -100 || center.Y > height + 100) continue;
            var tile = floor.Pathing[x, y];
            var color = ImGuiNET.ImGui.ColorConvertFloat4ToU32(tile.IsBlocked
                ? new Vector4(1, .2f, .15f, .7f) : tile.Properties.SurfaceLevelDelta != 0
                    ? new Vector4(1, .85f, .1f, .95f) : new Vector4(.1f, 1, .3f, .7f));
            draw.AddQuad(top, right, bottom, left, color, 1);
            if (!tile.IsBlocked)
                draw.AddText(center, color, $"{floor.WorldX + x},{floor.WorldY + y}");
        }

        Vector2 Point(int x, int y, sbyte elevation)
        {
            var iso = IsometricProjection.WorldToIso(floor.WorldX + x, floor.WorldY + y) +
                IsometricProjection.TileAnchorOffset;
            return transform.ToScreen(iso.X, iso.Y - TerrainElevationProjection.ScreenHeight(floor.BaseHeight +
                elevation * WorldElevationSampler.WorldHeightPerSample));
        }
    }
}
