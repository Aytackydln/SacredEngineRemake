using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Rendering;
using Sacred.World.Map;

namespace Sacred.Engine.Graphics.ImGui;

internal static class ImGuiWorldMapLabels
{
    // Ink on parchment, with a faint warm highlight rather than a bright HUD outline.
    private const uint Ink = 0xF21E241E;
    private const uint Highlight = 0x507CB4C8;

    public static void Draw(WorldMapFrame frame, int width, int height, ImFontPtr font)
    {
        if (frame.Annotations is not { } annotations) return;
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        var viewportCenter = new Vector2(width, height) * 0.5f;
        var mapStart = viewportCenter - frame.Center * frame.Zoom;
        var mapEnd = mapStart + new Vector2(frame.Map.Width, frame.Map.Height) * frame.Zoom;
        var clipMin = Vector2.Max(Vector2.Zero, mapStart);
        var clipMax = Vector2.Min(new(width, height), mapEnd);
        if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) return;
        // Keep the temporary straight strings inside ImGui's CPU clipping bounds;
        // apply the map's scissor rectangle after their vertices have been curved.
        draw.PushClipRectFullScreen();
        var firstCommand = draw.CmdBuffer.Size - 1;
        ImGuiNET.ImGui.PushFont(font);
        foreach (var label in annotations.Labels)
        {
            // The native label path starts at the center of its integer grid cell.
            var anchor = WorldMapProjection.WorldToMap(label.WorldPosition + new Vector2(0.5f), frame.Map.Width, frame.Kind);
            if (anchor.X < 0 || anchor.Y < 0 || anchor.X >= frame.Map.Width || anchor.Y >= frame.Map.Height) continue;
            var screen = viewportCenter + (anchor - frame.Center) * frame.Zoom;
            var scale = frame.Zoom * (frame.Map.Width / 2048.0f);
            var extent = 180 * scale;
            if (screen.X + extent < 0 || screen.Y + extent < 0 || screen.X - extent > width || screen.Y - extent > height) continue;
            var textWidth = ImGuiNET.ImGui.CalcTextSize(label.Text).X;
            DrawText(draw, font, label, textWidth, screen + new Vector2(0, 0.7f), scale, Highlight);
            DrawText(draw, font, label, textWidth, screen, scale, Ink);
        }
        ImGuiNET.ImGui.PopFont();
        var lastCommand = draw.CmdBuffer.Size - 1;
        draw.PopClipRect();
        for (var index = firstCommand; index <= lastCommand; index++)
            draw.CmdBuffer[index].ClipRect = new(clipMin.X, clipMin.Y, clipMax.X, clipMax.Y);
    }

    private static void DrawText(ImDrawListPtr draw, ImFontPtr font, WorldMapLabel label,
        float textWidth, Vector2 anchor, float scale, uint color)
    {
        // The native text API owns glyph lookup and atlas layout. This also avoids
        // exposing C++ glyph bitfields through the managed binding's struct layout.
        var first = draw.VtxBuffer.Size;
        draw.AddText(font, font.FontSize, Vector2.Zero, color, label.Text);
        for (var index = first; index < draw.VtxBuffer.Size; index++)
        {
            var vertex = draw.VtxBuffer[index];
            vertex.pos = anchor + WorldMapLabelTransform.Transform(vertex.pos, textWidth,
                label.Curvature, label.AngleDegrees) * scale;
        }
    }
}
