using System;
using System.Numerics;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Draws labels for the actual model instances submitted to the model pass.</summary>
internal static class ImGuiModelDebugRenderer
{
    private static readonly Vector4 LabelColour = new(0.45f, 1.0f, 0.82f, 1.0f);

    public static void Draw(SacredCamera camera, SceneState scene, int outputWidth, int outputHeight)
    {
        if (!scene.Debug.ModelNamesVisible)
            return;

        var drawList = DearImGui.GetBackgroundDrawList();
        var viewProjection = camera.View * camera.Projection;
        foreach (var model in scene.Models)
        {
            var clip = Vector4.Transform(new Vector4(model.VisualCenter, 1.0f), viewProjection);
            if (clip.W <= float.Epsilon)
                continue;

            var screen = new Vector2(
                (clip.X / clip.W * 0.5f + 0.5f) * outputWidth,
                (0.5f - clip.Y / clip.W * 0.5f) * outputHeight);
            if (screen.X < 0.0f || screen.X > outputWidth || screen.Y < 0.0f || screen.Y > outputHeight)
                continue;

            drawList.AddText(screen, Colour(LabelColour),
                $"{model.Name}\nP {model.Position.X:0.##},{model.Position.Y:0.##},{model.Position.Z:0.##}  " +
                $"R {model.Rotation.Z * (180.0f / MathF.PI):0.#}°  V{model.Mesh.Vertices.Length} I{model.Mesh.Indices.Length}");
        }
    }

    private static uint Colour(Vector4 colour) => DearImGui.ColorConvertFloat4ToU32(colour);
}
