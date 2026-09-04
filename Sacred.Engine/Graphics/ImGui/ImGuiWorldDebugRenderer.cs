using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.Core.World.Lighting;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.World.Geometry;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Projects decoded world records into interactive screen-space debug guides.</summary>
internal static class ImGuiWorldDebugRenderer
{
    private static readonly Vector4 TownColour = new(0.30f, 1.00f, 0.35f, 0.95f);
    private static readonly Vector4 IndoorColour = new(0.25f, 0.65f, 1.00f, 0.95f);
    private static readonly Vector4 TriggerColour = new(1.00f, 0.78f, 0.15f, 0.95f);
    private static readonly Vector4 RuntimeBlockedColour = new(1.00f, 0.25f, 0.70f, 0.95f);
    private static readonly Vector4 PropertyColour = new(0.94f, 0.55f, 0.20f, 0.95f);
    private static readonly Vector4 ShadowableColour = new(0.66f, 0.48f, 1.00f, 0.95f);
    private static readonly Vector4 FadeColour = new(0.20f, 0.92f, 0.88f, 0.95f);
    private static readonly Vector4 EntranceColour = new(1.00f, 1.00f, 1.00f, 0.95f);

    public static void Draw(
        SacredCamera camera,
        VisibleWorld world,
        SceneDebugState debug,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        IReadOnlyList<TerrainWorldLight> worldLights,
        int renderWidth,
        int renderHeight)
    {
        if (!AnyWorldDiagnosticVisible(debug))
            return;

        var transform = IsometricProjection.CreateScreenTransform(
            camera.WorldCenter,
            camera.ViewportZoom,
            renderWidth,
            renderHeight);
        var drawList = DearImGui.GetBackgroundDrawList();

        if (AnyTileDiagnosticVisible(debug))
            DrawTileDiagnostics(drawList, transform, world, debug, renderWidth, renderHeight);
        if (debug.SectorBoundsVisible)
            DrawSectorBounds(drawList, transform, world);
        if (debug.WorldLightBoundsVisible)
            DrawLightBounds(drawList, transform, worldLights);
        if (debug.StaticSpriteBoundsVisible)
            DrawStaticBounds(drawList, transform, staticSprites);
    }

    private static bool AnyWorldDiagnosticVisible(SceneDebugState debug) =>
        AnyTileDiagnosticVisible(debug) || debug.SectorBoundsVisible ||
        debug.WorldLightBoundsVisible || debug.StaticSpriteBoundsVisible;

    private static bool AnyTileDiagnosticVisible(SceneDebugState debug) =>
        debug.TileCoordinatesVisible || debug.TownTilesVisible || debug.IndoorTilesVisible ||
        debug.TriggerTilesVisible || debug.RuntimeBlockedTilesVisible || debug.MovementFlagTilesVisible ||
        debug.ShadowableTilesVisible || debug.ModelFadeTilesVisible || debug.EntranceTilesVisible ||
        debug.TerrainSurfacesVisible || debug.VisualElevationVisible ||
        debug.GameplayElevationVisible || debug.BakedLightingVisible;

    private static void DrawTileDiagnostics(
        ImDrawListPtr drawList,
        WorldScreenTransform transform,
        VisibleWorld world,
        SceneDebugState debug,
        int renderWidth,
        int renderHeight)
    {
        foreach (var sector in world.Sectors)
        for (var localY = 0; localY < Sector.TileCount; localY++)
        for (var localX = 0; localX < Sector.TileCount; localX++)
        {
            var points = GetTilePoints(transform, sector, localX, localY);
            if (!IsVisible(points, renderWidth, renderHeight))
                continue;

            var tile = sector.Pathing[localX, localY];
            if (debug.TownTilesVisible && tile.Flags.HasFlag(WorldPathFlags.Town))
                AddQuad(drawList, points, TownColour, 2.2f);
            if (debug.IndoorTilesVisible && tile.Flags.HasFlag(WorldPathFlags.Indoor))
                AddQuad(drawList, points, IndoorColour, 2.2f);
            if (debug.TriggerTilesVisible && tile.Flags.HasFlag(WorldPathFlags.Trigger))
                AddQuad(drawList, points, TriggerColour, 2.2f);
            if (debug.RuntimeBlockedTilesVisible && tile.Flags.HasFlag(WorldPathFlags.RuntimeBlocked))
                AddQuad(drawList, points, RuntimeBlockedColour, 2.2f);
            if (debug.MovementFlagTilesVisible && tile.Properties.BlocksMovement)
                AddQuad(drawList, points, PropertyColour, 2.2f);
            if (debug.ShadowableTilesVisible && tile.Properties.IsShadowable)
                AddQuad(drawList, points, ShadowableColour, 2.2f);
            if (debug.ModelFadeTilesVisible && tile.Properties.CanFadeModelsBehind)
                AddQuad(drawList, points, FadeColour, 2.2f);
            if (debug.EntranceTilesVisible && tile.Properties.IsEntranceBoundary)
                AddQuad(drawList, points, EntranceColour, 3.0f);

            var worldX = sector.Coord.X * Sector.TileCount + localX;
            var worldY = sector.Coord.Y * Sector.TileCount + localY;
            var center = (points.Left + points.Top + points.Right + points.Bottom) * 0.25f;
            var label = BuildTileLabel(sector, tile, debug, localX, localY, worldX, worldY);
            if (label.Length > 0)
                drawList.AddText(center, Colour(new Vector4(1.0f, 0.94f, 0.72f, 1.0f)), label);

            if (debug.BakedLightingVisible)
                DrawBakedLightSamples(drawList, points, sector.BakedLight[localX, localY]);
        }
    }

    private static string BuildTileLabel(
        Sector sector,
        WorldPathTile tile,
        SceneDebugState debug,
        int localX,
        int localY,
        int worldX,
        int worldY)
    {
        var label = string.Empty;
        if (debug.TileCoordinatesVisible)
            label = $"{worldX},{worldY}";
        if (debug.TerrainSurfacesVisible)
            label = Append(label, $"S:{(byte)tile.TerrainSurface:X2}");
        if (debug.VisualElevationVisible)
        {
            var e = sector.VisualElevation[localX, localY];
            label = Append(label, $"V:{e.NorthWest}/{e.NorthEast}/{e.SouthWest}/{e.SouthEast}");
        }
        if (debug.GameplayElevationVisible)
        {
            var e = sector.Elevation[localX, localY];
            label = Append(label, $"Z:{e.NorthWest}/{e.NorthEast}/{e.SouthWest}/{e.SouthEast}");
        }
        return label;
    }

    private static string Append(string current, string value) =>
        current.Length == 0 ? value : current + "\n" + value;

    private static TilePoints GetTilePoints(
        WorldScreenTransform transform,
        Sector sector,
        int localX,
        int localY)
    {
        var worldX = sector.Coord.X * Sector.TileCount + localX;
        var worldY = sector.Coord.Y * Sector.TileCount + localY;
        var iso = IsometricProjection.WorldToIso(worldX, worldY);
        var elevation = sector.VisualElevation[localX, localY];
        return new TilePoints(
            transform.ToScreen(iso.X, iso.Y + 24.0f - elevation.SouthWest),
            transform.ToScreen(iso.X + 48.0f, iso.Y - elevation.NorthWest),
            transform.ToScreen(iso.X + 96.0f, iso.Y + 24.0f - elevation.NorthEast),
            transform.ToScreen(iso.X + 48.0f, iso.Y + 48.0f - elevation.SouthEast));
    }

    private static bool IsVisible(TilePoints points, int width, int height)
    {
        var minimumX = MathF.Min(MathF.Min(points.Left.X, points.Top.X), MathF.Min(points.Right.X, points.Bottom.X));
        var maximumX = MathF.Max(MathF.Max(points.Left.X, points.Top.X), MathF.Max(points.Right.X, points.Bottom.X));
        var minimumY = MathF.Min(MathF.Min(points.Left.Y, points.Top.Y), MathF.Min(points.Right.Y, points.Bottom.Y));
        var maximumY = MathF.Max(MathF.Max(points.Left.Y, points.Top.Y), MathF.Max(points.Right.Y, points.Bottom.Y));
        return maximumX >= 0.0f && minimumX <= width && maximumY >= 0.0f && minimumY <= height;
    }

    private static void AddQuad(ImDrawListPtr drawList, TilePoints points, Vector4 colour, float thickness) =>
        drawList.AddQuad(points.Left, points.Top, points.Right, points.Bottom, Colour(colour), thickness);

    private static void DrawBakedLightSamples(
        ImDrawListPtr drawList,
        TilePoints points,
        TerrainBakedLightTile light)
    {
        AddLightSample(drawList, points.Top, light.NorthWest);
        AddLightSample(drawList, points.Right, light.NorthEast);
        AddLightSample(drawList, points.Left, light.SouthWest);
        AddLightSample(drawList, points.Bottom, light.SouthEast);
    }

    private static void AddLightSample(ImDrawListPtr drawList, Vector2 position, byte brightness)
    {
        var value = brightness / 255.0f;
        drawList.AddCircleFilled(position, 3.5f, Colour(new Vector4(value, value, value, 1.0f)));
        drawList.AddCircle(position, 4.5f, Colour(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));
    }

    private static void DrawSectorBounds(ImDrawListPtr drawList, WorldScreenTransform transform, VisibleWorld world)
    {
        var colour = Colour(new Vector4(1.0f, 0.35f, 0.1f, 0.95f));
        foreach (var sector in world.Sectors)
        {
            var x = sector.Coord.X * Sector.TileCount;
            var y = sector.Coord.Y * Sector.TileCount;
            var northIso = IsometricProjection.WorldToIso(x, y);
            var eastIso = IsometricProjection.WorldToIso(x + Sector.TileCount, y);
            var southIso = IsometricProjection.WorldToIso(x + Sector.TileCount, y + Sector.TileCount);
            var westIso = IsometricProjection.WorldToIso(x, y + Sector.TileCount);
            var north = transform.ToScreen(northIso.X + 48.0f, northIso.Y);
            var east = transform.ToScreen(eastIso.X + 48.0f, eastIso.Y);
            var south = transform.ToScreen(southIso.X + 48.0f, southIso.Y);
            var west = transform.ToScreen(westIso.X + 48.0f, westIso.Y);
            drawList.AddQuad(north, east, south, west, colour, 3.0f);
            drawList.AddText(north + new Vector2(5.0f), colour, $"sector {sector.Coord.X},{sector.Coord.Y}");
        }
    }

    private static void DrawLightBounds(
        ImDrawListPtr drawList,
        WorldScreenTransform transform,
        IReadOnlyList<TerrainWorldLight> lights)
    {
        foreach (var light in lights)
        {
            var topLeft = transform.ToScreen(light.IsoX, light.IsoY);
            var diameter = transform.Scale(light.Diameter);
            var center = topLeft + new Vector2(diameter * 0.5f);
            var colour = light.Shape == WorldLightShape.SurfaceIllumination
                ? new Vector4(1.0f, 0.65f, 0.12f, 0.95f)
                : new Vector4(light.Colour, 0.95f);
            drawList.AddCircle(center, diameter * 0.5f, Colour(colour), 48, 2.0f);
            drawList.AddLine(center - new Vector2(6.0f, 0.0f), center + new Vector2(6.0f, 0.0f), Colour(colour), 2.0f);
            drawList.AddLine(center - new Vector2(0.0f, 6.0f), center + new Vector2(0.0f, 6.0f), Colour(colour), 2.0f);
            drawList.AddText(center + new Vector2(7.0f), Colour(colour), $"{light.Shape} D{light.Diameter:0} A{light.Opacity:0.00}");
        }
    }

    private static void DrawStaticBounds(
        ImDrawListPtr drawList,
        WorldScreenTransform transform,
        IReadOnlyList<TerrainStaticSprite> sprites)
    {
        var boundsColour = Colour(new Vector4(0.25f, 1.0f, 0.75f, 0.85f));
        var anchorColour = Colour(new Vector4(1.0f, 0.25f, 0.25f, 1.0f));
        foreach (var sprite in sprites)
        {
            var topLeft = transform.ToScreen(sprite.IsoX, sprite.IsoY);
            var bottomRight = topLeft + new Vector2(
                transform.Scale(sprite.RenderWidth),
                transform.Scale(sprite.RenderHeight));
            drawList.AddRect(topLeft, bottomRight, boundsColour);
            var anchor = transform.ToScreen(sprite.DepthX, sprite.DepthY);
            drawList.AddCircleFilled(anchor, 3.0f, anchorColour);
        }
    }

    private static uint Colour(Vector4 colour) => DearImGui.ColorConvertFloat4ToU32(colour);

    private readonly record struct TilePoints(Vector2 Left, Vector2 Top, Vector2 Right, Vector2 Bottom);
}
