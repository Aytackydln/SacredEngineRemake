using System;
using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Scene;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Read-only top-left multiplayer HUD, independent of debug overlays.</summary>
internal static class Dx12AllyHudPass
{
    public static void Build(SceneState scene, Vector2 viewport, float hudScale,
        Action<uint?, ImDrawListPtr, Vector2, Vector2> drawPortrait)
    {
        if (scene.EscapeMenu is { IsOpen: true } || scene.Allies.Count == 0) return;
        var draw = DearImGui.GetBackgroundDrawList();
        var scale = Math.Clamp(hudScale, .75f, 1.5f);
        var portraitSize = new Vector2(36, 42) * scale;
        var step = 49 * scale;
        var origin = new Vector2(15, 15) * scale;
        foreach (var ally in scene.Allies)
        {
            var portrait = origin + new Vector2(9, 0) * scale;
            var end = portrait + portraitSize;
            if (end.Y > viewport.Y) break;
            draw.AddRectFilled(portrait - Vector2.One * 2, end + Vector2.One * 2, 0xEB1B1713, 4);
            drawPortrait(ally.ActorType, draw, portrait, end);
            draw.AddRect(portrait - Vector2.One, end + Vector2.One, 0xFF63859C, 3);
            if (ally.IsLeader)
                draw.AddCircleFilled(portrait + new Vector2(3, 3) * scale, 3 * scale, 0xFF74C4EF);
            if (ally.HealthFraction is { } health)
            {
                var railEnd = new Vector2(origin.X + 5 * scale, end.Y);
                draw.AddRectFilled(origin, railEnd, 0xFF241B25, 2);
                if (health > 0)
                    draw.AddRectFilled(new(origin.X, end.Y - portraitSize.Y * health), railEnd, 0xFF4546C7, 2);
            }
            var label = new Vector2(end.X + 7 * scale, origin.Y + (portraitSize.Y - DearImGui.GetFontSize()) / 2);
            var textRight = Math.Min(viewport.X, label.X + 150 * scale);
            if (textRight > label.X)
            {
                draw.PushClipRect(new(label.X, origin.Y), new(textRight, end.Y), true);
                draw.AddText(label + Vector2.One, 0xFF171211, ally.Name);
                draw.AddText(label, 0xFFF4EEE5, ally.Name);
                draw.PopClipRect();
            }
            origin.Y += step;
        }
    }
}
