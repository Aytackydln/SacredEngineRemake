using System;
using Sacred.Engine.Graphics.Models;

namespace Sacred.Engine.Scene.InGame;

/// <summary>On-demand visibility diagnostics without changing model or animation state.</summary>
internal static class ModelVisibilityCheats
{
    public static bool TryExecute(string option, string value, SceneState scene, SacredCamera camera, out string message)
    {
        message = string.Empty;
        if (!option.Equals("model-visibility", StringComparison.OrdinalIgnoreCase)) return false;
        var count = 0;
        foreach (var model in scene.Models)
        {
            if (!value.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                !model.Name.Contains(value, StringComparison.OrdinalIgnoreCase)) continue;
            var animation = model.Geometry.Animation;
            var bounds = model.VisibilityBounds;
            EngineLog.WriteLine($"Model visibility: {model.Name}; position {model.Position}; kind {model.Geometry.Kind}; frustum {ModelFrustumCuller.IsVisible(camera, model)}; bounds {bounds.Center}/{bounds.Radius}; vertices {model.Geometry.BindMesh.Vertices.Length}, indices {model.Geometry.BindMesh.Indices.Length}; pose {animation?.Pose.Revision}, sample {animation?.Pose.SampleTimeSeconds}, clip {animation?.Pose.Animation?.Name}; CPU current {animation?.IsCpuMeshCurrent}.");
            count++;
        }
        message = $"listed {count} models; use set model-visibility <all|name/reference substring>";
        return true;
    }
}
