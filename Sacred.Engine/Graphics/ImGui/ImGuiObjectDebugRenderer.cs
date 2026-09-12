using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.World.Geometry;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Draws static-object diagnostics and resolves their interactive hover target.</summary>
internal static class ImGuiObjectDebugRenderer
{
    private const float StaticObjectShiftX = 47.8f;
    private const float StaticObjectShiftY = -0.3f;
    private const float MinimumHoverRadius = 8.0f;
    private static readonly Vector4 OutlineColour = new(1.0f, 0.82f, 0.18f, 1.0f);

    public static void Draw(
        ImDrawListPtr drawList,
        WorldScreenTransform transform,
        VisibleWorld world,
        SceneDebugState debug,
        AssetManager assets,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        int renderWidth,
        int renderHeight)
    {
        uint? hoveredStaticObjectId = null;
        var closestHoverDistanceSquared = float.MaxValue;
        var mousePosition = DearImGui.GetMousePos();
        var canHoverWorld = !DearImGui.GetIO().WantCaptureMouse;

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

                DrawRing(drawList, anchor, ringIndex++, option.Colour);
            }

            var hasItemDot = false;
            var item = assets.GetItem(staticObject.TypeId);
            if (debug.VisibleItemGraphicFlags != SacredItemGraphicFlags.None && item is { } graphicItem)
            {
                foreach (var option in WorldDebugFlagCatalog.ItemGraphicFlags)
                {
                    if (!debug.VisibleItemGraphicFlags.HasFlag(option.Flag) ||
                        !graphicItem.ModelDesc.GraphicFlags.HasFlag(option.Flag))
                    {
                        continue;
                    }

                    DrawRing(drawList, anchor, ringIndex++, option.Colour);
                    hasItemDot = true;
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
                    DrawRing(drawList, anchor, ringIndex++, Vector4.One with { W = 0.95f });
                    rawByteMatched = true;
                    hasItemDot = true;
                }

                foreach (var option in WorldDebugFlagCatalog.ItemDescriptorByteFlags)
                {
                    if ((debug.VisibleItemDescriptorByteBits & option.Flag) == 0 ||
                        (rawByte & option.Flag) == 0)
                    {
                        continue;
                    }

                    DrawRing(drawList, anchor, ringIndex++, option.Colour);
                    rawByteMatched = true;
                    hasItemDot = true;
                }

                hasItemDot |= debug.ItemDescriptorByteValuesVisible;
            }

            if (ringIndex == 0 && !debug.ItemDescriptorByteValuesVisible)
                continue;

            drawList.AddCircleFilled(anchor, 2.5f, Colour(Vector4.One));
            if (item is { } labelItem && (debug.ItemDescriptorByteValuesVisible || rawByteMatched))
            {
                drawList.AddText(
                    anchor + new Vector2(8.0f, 4.0f + ringIndex * 3.0f),
                    Colour(new Vector4(1.0f, 0.94f, 0.72f, 1.0f)),
                    $"item {labelItem.ItemIndex} {labelItem.ModelName}\n" +
                    $"[0x{debug.ItemDescriptorByteOffset:X2}]=0x{rawByte:X2}");
            }

            if (!canHoverWorld || !hasItemDot)
                continue;

            var hoverRadius = MathF.Max(MinimumHoverRadius, 6.0f + Math.Max(0, ringIndex - 1) * 3.0f);
            var hoverDistanceSquared = Vector2.DistanceSquared(mousePosition, anchor);
            if (hoverDistanceSquared <= hoverRadius * hoverRadius &&
                hoverDistanceSquared < closestHoverDistanceSquared)
            {
                closestHoverDistanceSquared = hoverDistanceSquared;
                hoveredStaticObjectId = staticObject.StaticId;
            }
        }

        if (hoveredStaticObjectId is not { } hoveredId ||
            !TryFindSprite(staticSprites, hoveredId, out var hoveredSprite))
        {
            return;
        }

        debug.HoveredStaticObjectId = hoveredId;
        DrawHoveredOutline(drawList, transform, hoveredSprite);
    }

    private static void DrawRing(ImDrawListPtr drawList, Vector2 anchor, int ringIndex, Vector4 colour) =>
        drawList.AddCircle(anchor, 6.0f + ringIndex * 3.0f, Colour(colour), 16, 2.0f);

    private static bool TryFindSprite(
        IReadOnlyList<TerrainStaticSprite> sprites,
        uint staticObjectId,
        out TerrainStaticSprite sprite)
    {
        foreach (var candidate in sprites)
        {
            if (candidate.StaticObjectId != staticObjectId)
                continue;

            sprite = candidate;
            return true;
        }

        sprite = default;
        return false;
    }

    private static void DrawHoveredOutline(
        ImDrawListPtr drawList,
        WorldScreenTransform transform,
        TerrainStaticSprite sprite)
    {
        var topLeft = transform.ToScreen(sprite.IsoX, sprite.IsoY);
        var bottomRight = topLeft + new Vector2(
            transform.Scale(sprite.RenderWidth),
            transform.Scale(sprite.RenderHeight));
        drawList.AddRect(topLeft, bottomRight, Colour(new Vector4(0.0f, 0.0f, 0.0f, 0.9f)), 0.0f, ImDrawFlags.None, 5.0f);
        drawList.AddRect(topLeft, bottomRight, Colour(OutlineColour), 0.0f, ImDrawFlags.None, 2.0f);
    }

    private static uint Colour(Vector4 colour) => DearImGui.ColorConvertFloat4ToU32(colour);
}
