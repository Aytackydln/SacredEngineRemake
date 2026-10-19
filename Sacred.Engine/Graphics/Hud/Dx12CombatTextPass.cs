using System;
using System.Globalization;
using System.Numerics;
using Sacred.Engine.Scene;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>World-anchored readouts for received damage notifications, in the normal output UI pass.</summary>
internal static class Dx12CombatTextPass
{
    public static void Build(SceneState scene, int width, int height)
    {
        scene.CombatText.DrawnCount = 0;
        if (scene.NpcOverheads.Camera is not { } camera || scene.EscapeMenu is { IsOpen: true }) return;
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        draw.PushClipRect(Vector2.Zero, new(width, height), true);
        foreach (var text in scene.CombatText.Damage)
        {
            var center = text.Model.VisualCenter;
            var clip = Vector4.Transform(new Vector4(center, 1), camera.View * camera.Projection);
            if (clip.W <= 0 || !float.IsFinite(clip.X) || !float.IsFinite(clip.Y)) continue;
            var position = new Vector2((clip.X / clip.W + 1) * width * .5f, (1 - clip.Y / clip.W) * height * .5f);
            position.Y -= 24 + text.AgeSeconds * 36;
            if (position.X < -32 || position.X > width + 32 || position.Y < -32 || position.Y > height + 32) continue;
            var label = text.Amount.ToString(CultureInfo.InvariantCulture);
            position.X -= ImGuiNET.ImGui.CalcTextSize(label).X * .5f;
            var alpha = (uint)(255 * Math.Clamp((1 - text.AgeSeconds) * 2, 0, 1));
            draw.AddText(position + Vector2.One, alpha << 24, label);
            draw.AddText(position, (alpha << 24) | 0x005050ff, label);
            scene.CombatText.DrawnCount++;
        }
        draw.PopClipRect();
    }
}
