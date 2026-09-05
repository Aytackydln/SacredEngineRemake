using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.Core.World.Lighting;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.World.Geometry;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Projects decoded world records into interactive screen-space debug guides.</summary>
internal static class ImGuiWorldDebugRenderer
{
    private const float StaticObjectShiftX = 47.8f;
    private const float StaticObjectShiftY = -0.3f;
    private static readonly Vector4 PropertyColour = new(0.94f, 0.55f, 0.20f, 0.95f);
    private static readonly Vector4 EntranceColour = new(1.00f, 1.00f, 1.00f, 0.95f);

    public static void Draw(
        SacredCamera camera,
        VisibleWorld world,
        SceneDebugState debug,
        AssetManager assets,
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
        if (debug.VisibleSectorFlags != SectorEnvironmentFlags.None)
            DrawSectorFlagDiagnostics(drawList, transform, world, debug);
        if (debug.WorldLightBoundsVisible)
            DrawLightBounds(drawList, transform, worldLights);
        if (debug.StaticSpriteBoundsVisible)
            DrawStaticBounds(drawList, transform, staticSprites);
        if (debug.VisibleStaticObjectFlags != StaticObjectFlags.None ||
            debug.VisibleItemGraphicFlags != SacredItemGraphicFlags.None ||
            debug.VisibleItemDescriptorByteBits != 0 ||
            debug.ItemDescriptorByteMatchEnabled ||
            debug.ItemDescriptorByteValuesVisible)
        {
            DrawObjectFlagDiagnostics(drawList, transform, world, debug, assets, renderWidth, renderHeight);
        }
    }

    private static bool AnyWorldDiagnosticVisible(SceneDebugState debug) =>
        AnyTileDiagnosticVisible(debug) || debug.SectorBoundsVisible ||
        debug.VisibleSectorFlags != SectorEnvironmentFlags.None ||
        debug.WorldLightBoundsVisible || debug.StaticSpriteBoundsVisible ||
        debug.VisibleStaticObjectFlags != StaticObjectFlags.None ||
        debug.VisibleItemGraphicFlags != SacredItemGraphicFlags.None ||
        debug.VisibleItemDescriptorByteBits != 0 ||
        debug.ItemDescriptorByteMatchEnabled ||
        debug.ItemDescriptorByteValuesVisible;

    private static bool AnyTileDiagnosticVisible(SceneDebugState debug) =>
        debug.TileCoordinatesVisible || debug.VisiblePathFlags != WorldPathFlags.None ||
        debug.VisibleTileFlags != WldxTileFlags.None ||
        debug.VisibleSurfaceFlags != WldxTerrainSurface.Default ||
        debug.MovementFlagTilesVisible || debug.EntranceTilesVisible ||
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
            foreach (var option in WorldDebugFlagCatalog.PathFlags)
            {
                if (debug.VisiblePathFlags.HasFlag(option.Flag) && tile.Flags.HasFlag(option.Flag))
                    AddQuad(drawList, points, option.Colour, 2.2f);
            }
            foreach (var option in WorldDebugFlagCatalog.TileFlags)
            {
                if (debug.VisibleTileFlags.HasFlag(option.Flag) && tile.TileFlags.HasFlag(option.Flag))
                    AddQuad(drawList, points, option.Colour, 2.2f);
            }
            foreach (var option in WorldDebugFlagCatalog.SurfaceFlags)
            {
                if (debug.VisibleSurfaceFlags.HasFlag(option.Flag) && tile.TerrainSurface.HasFlag(option.Flag))
                    AddQuad(drawList, points, option.Colour, 2.2f);
            }
            if (debug.MovementFlagTilesVisible && tile.Properties.BlocksMovement)
                AddQuad(drawList, points, PropertyColour, 2.2f);
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

    private static void DrawSectorFlagDiagnostics(
        ImDrawListPtr drawList,
        WorldScreenTransform transform,
        VisibleWorld world,
        SceneDebugState debug)
    {
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

            var matchCount = 0;
            foreach (var option in WorldDebugFlagCatalog.SectorFlags)
            {
                if (!debug.VisibleSectorFlags.HasFlag(option.Flag) ||
                    !sector.EnvironmentFlags.HasFlag(option.Flag))
                {
                    continue;
                }

                drawList.AddQuad(north, east, south, west, Colour(option.Colour), 2.0f + matchCount);
                matchCount++;
            }
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

    private static void DrawObjectFlagDiagnostics(
        ImDrawListPtr drawList,
        WorldScreenTransform transform,
        VisibleWorld world,
        SceneDebugState debug,
        AssetManager assets,
        int renderWidth,
        int renderHeight)
    {
        foreach (var sector in world.Sectors)
        foreach (var staticObject in sector.StaticObjects.Objects)
        {
            var anchor = transform.ToScreen(
                staticObject.ProjectedX + StaticObjectShiftX,
                staticObject.ProjectedY + StaticObjectShiftY);
            if (anchor.X < 0.0f || anchor.X > renderWidth || anchor.Y < 0.0f || anchor.Y > renderHeight)
                continue;

            var ringIndex = 0;
            foreach (var option in WorldDebugFlagCatalog.StaticFlags)
            {
                if (!debug.VisibleStaticObjectFlags.HasFlag(option.Flag) ||
                    !staticObject.Flags.HasFlag(option.Flag))
                {
                    continue;
                }

                drawList.AddCircle(anchor, 6.0f + ringIndex * 3.0f, Colour(option.Colour), 16, 2.0f);
                ringIndex++;
            }

            var item = assets.GetItem(staticObject.TypeId);
            if (debug.VisibleItemGraphicFlags != SacredItemGraphicFlags.None && item is { } graphicItem)
            {
                foreach (var option in WorldDebugFlagCatalog.ItemGraphicFlags)
                {
                    if (!debug.VisibleItemGraphicFlags.HasFlag(option.Flag) ||
                        !graphicItem.GraphicFlags.HasFlag(option.Flag))
                    {
                        continue;
                    }

                    drawList.AddCircle(anchor, 6.0f + ringIndex * 3.0f, Colour(option.Colour), 16, 2.0f);
                    ringIndex++;
                }
            }

            var rawByteMatched = false;
            byte rawByte = 0;
            if (item is { } descriptorItem &&
                (debug.VisibleItemDescriptorByteBits != 0 ||
                 debug.ItemDescriptorByteMatchEnabled ||
                 debug.ItemDescriptorByteValuesVisible))
            {
                rawByte = descriptorItem.ModelDesc.GetRawByte(debug.ItemDescriptorByteOffset);
                if (debug.ItemDescriptorByteMatchEnabled &&
                    rawByte == debug.ItemDescriptorByteMatchValue)
                {
                    drawList.AddCircle(
                        anchor,
                        6.0f + ringIndex * 3.0f,
                        Colour(new Vector4(1.0f, 1.0f, 1.0f, 0.95f)),
                        16,
                        2.0f);
                    ringIndex++;
                    rawByteMatched = true;
                }
                foreach (var option in WorldDebugFlagCatalog.ItemDescriptorByteFlags)
                {
                    if ((debug.VisibleItemDescriptorByteBits & option.Flag) == 0 ||
                        (rawByte & option.Flag) == 0)
                    {
                        continue;
                    }

                    drawList.AddCircle(anchor, 6.0f + ringIndex * 3.0f, Colour(option.Colour), 16, 2.0f);
                    ringIndex++;
                    rawByteMatched = true;
                }
            }

            if (ringIndex == 0 && !debug.ItemDescriptorByteValuesVisible)
                continue;

            drawList.AddCircleFilled(anchor, 2.5f, Colour(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));
            if (item is { } labelItem && (debug.ItemDescriptorByteValuesVisible || rawByteMatched))
            {
                drawList.AddText(
                    anchor + new Vector2(8.0f, 4.0f + ringIndex * 3.0f),
                    Colour(new Vector4(1.0f, 0.94f, 0.72f, 1.0f)),
                    $"item {labelItem.ItemIndex} {labelItem.ModelName}\n" +
                    $"[0x{debug.ItemDescriptorByteOffset:X2}]=0x{rawByte:X2}");
            }
        }
    }

    private static uint Colour(Vector4 colour) => DearImGui.ColorConvertFloat4ToU32(colour);

    private readonly record struct TilePoints(Vector2 Left, Vector2 Top, Vector2 Right, Vector2 Bottom);
}
