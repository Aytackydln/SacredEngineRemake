using System;
using System.Numerics;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Queries the requested visual pose, including when the renderer evaluates it on the GPU.</summary>
internal static class CreatureModelCheats
{
    public static bool TrySetOption(string option, string value, SceneModel? model, out string message)
    {
        message = string.Empty;
        if (!option.Equals("model-pivot", StringComparison.OrdinalIgnoreCase)) return false;
        if (!value.Equals("status", StringComparison.OrdinalIgnoreCase))
        { message = "Use set model-pivot status"; return true; }
        if (model?.Geometry.Animation is not { } mesh || mesh.Playback.LastRequest is not { } request)
        { message = "Creature animation is not ready"; return true; }
        var evaluator = new GrnPoseEvaluator(mesh.Pose.Data, request.Animation);
        var pose = request.Blend is { } blend ? evaluator.EvaluateBlended(request.TimeSeconds, request.TimeMode, blend)
            : request.TimeMode == GrnPoseTimeMode.Clamp ? evaluator.EvaluateClamped(request.TimeSeconds) : evaluator.Evaluate(request.TimeSeconds);
        string Offset(string name) => pose.Data.TryFindBone(name, out var bone)
            ? (Vector3.Transform(pose.Data.Projection.Project(pose.BoneWorldTransforms[bone].Translation), model.Transform)
                - model.RenderPosition).ToString("F4") : "absent";
        message = $"Creature pivot: tile {model.Position}; facing {model.Rotation.Z * 180 / MathF.PI:F3} degrees; " +
            $"clip {request.Animation.Name} at {request.TimeSeconds:F4}s; root offset {Offset("__Root")}; " +
            $"left foot offset {Offset("Bip01 L Foot")}; right foot offset {Offset("Bip01 R Foot")}";
        return true;
    }
}
