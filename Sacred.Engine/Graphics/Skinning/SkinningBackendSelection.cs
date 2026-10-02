using System;
using System.Collections.Generic;
using Sacred.Engine.Scene;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Selects a ready backend without exposing stale CPU vertices or unfinished GPU copies.</summary>
internal sealed class SkinningBackendSelection
{
    private readonly record struct Decision(SkinningMode Mode, bool Gpu, string? Reason);
    private readonly HashSet<GrnPose> _active = new(ReferenceEqualityComparer.Instance);
    private readonly List<GrnPose> _removed = [];
    private readonly Dictionary<GrnPose, Decision> _decisions = new(ReferenceEqualityComparer.Instance);
    public SkinningMode Mode { get; set; } = SkinningMode.Auto;
    public int GpuModelCount { get; private set; }
    public int CpuModelCount { get; private set; }

    public void Apply(SceneState scene, bool pipelinesReady, string? pipelineFailure,
        Func<SceneModelGeometry, bool> ready, Func<SceneModelGeometry, bool> failed, Action<string> log)
    {
        _active.Clear();
        GpuModelCount = CpuModelCount = 0;
        for (var i = 0; i < scene.Models.Count; i++)
        {
            var geometry = scene.Models[i].Geometry;
            if (geometry.Animation is not { } animation) continue;
            _active.Add(animation.Pose);
            var reason = Mode == SkinningMode.Cpu ? "explicit CPU selection" :
                !pipelinesReady ? pipelineFailure ?? "skeletal pipelines unavailable" :
                failed(geometry) ? "GPU source preparation failed" :
                !animation.Pose.IsEvaluated ? "pose not evaluated" :
                !ready(geometry) ? "GPU source/palette preparation pending" : null;
            var gpu = reason is null;
            if (gpu) GpuModelCount++; else CpuModelCount++;
            // Enabling CPU materialization prepares the current pose, including held endpoints.
            animation.MaterializeCpuVertices = !gpu;
            var desired = gpu ? SceneModelGeometryKind.GpuSkinned : SceneModelGeometryKind.CpuDeformed;
            if (geometry.Kind != desired)
                scene.SetModelGeometry(i, gpu ? SceneModelGeometry.ForGpuSkinning(animation) : SceneModelGeometry.ForCpuSkinning(animation));
            var decision = new Decision(Mode, gpu, reason);
            if (!_decisions.TryGetValue(animation.Pose, out var previous) || previous != decision)
            {
                _decisions[animation.Pose] = decision;
                log($"Skinning backend: {Mode}: {(gpu ? "GPU; pipeline and source/palette gates passed" : $"CPU; {reason}")}.");
            }
        }
        _removed.Clear();
        foreach (var pose in _decisions.Keys)
            if (!_active.Contains(pose)) _removed.Add(pose);
        foreach (var pose in _removed) _decisions.Remove(pose);
    }
}
