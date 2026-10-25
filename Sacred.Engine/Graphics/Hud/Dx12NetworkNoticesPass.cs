using System;
using System.Linq;
using System.Numerics;
using Sacred.Engine.Scene;
using Sacred.UI.Hud;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Screen-space transient notices, independent of the loaded actor model and camera.</summary>
internal static class Dx12NetworkNoticesPass
{
    public static void Build(SceneState scene, int width, int height)
    {
        scene.CombatText.DrawnHudNotices = 0;
        scene.CombatText.DrawnChatLines = 0;
        if (scene.EscapeMenu is { IsOpen: true }) return;
        var draw = ImGuiNET.ImGui.GetForegroundDrawList();
        var scale = BottomHudLayout.GetScale(new(width, height));
        var y = height * .25f;
        draw.PushClipRect(Vector2.Zero, new(width, height), true);
        foreach (var notice in scene.CombatText.HudNotices)
        {
            if (notice.Mode != 16) continue;
            var size = ImGuiNET.ImGui.CalcTextSize(notice.Text);
            var position = new Vector2((width - size.X) * .5f, y);
            var color = ToAbgr(notice.ColorArgb);
            draw.AddText(position + Vector2.One, color & 0xFF000000, notice.Text);
            draw.AddText(position, color, notice.Text);
            y += size.Y + 4 * scale; scene.CombatText.DrawnHudNotices++;
        }
        var chatWidth = MathF.Min(width - 32 * scale, 480 * scale);
        var chatBottom = height - 115 * scale;
        var lines = scene.CombatText.HudNotices.Where(n => n.Mode != 16).Concat(scene.CombatText.ChatLines).OrderBy(n => n.Order);
        // Newest lines remain above the bottom HUD; older lines scroll upwards and clip.
        var visible = lines.TakeLast(8).ToArray();
        var sizes = visible.Select(n => ImGuiNET.ImGui.CalcTextSize(n.Text, false, chatWidth)).ToArray();
        y = chatBottom - sizes.Sum(s => s.Y + 3 * scale);
        for (var i = 0; i < visible.Length; i++)
        {
            var notice = visible[i]; var position = new Vector2(16 * scale, y);
            var color = ToAbgr(notice.ColorArgb);
            draw.AddText(ImGuiNET.ImGui.GetFont(), ImGuiNET.ImGui.GetFontSize(), position + Vector2.One,
                color & 0xFF000000, notice.Text, chatWidth);
            draw.AddText(ImGuiNET.ImGui.GetFont(), ImGuiNET.ImGui.GetFontSize(), position, color, notice.Text, chatWidth);
            y += sizes[i].Y + 3 * scale; scene.CombatText.DrawnChatLines++;
        }
        draw.PopClipRect();
    }
    private static uint ToAbgr(uint argb) => (argb & 0xFF00FF00) | (argb & 0xFF) << 16 | (argb >> 16 & 0xFF);
}
