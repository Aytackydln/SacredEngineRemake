using System;
using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Engine.Scene;

public enum SceneModelGeometryKind { Static, CpuDeformed, GpuSkinned }

/// <summary>Explicit geometry ownership. GPU bind vertices cannot enter the CPU renderer accidentally.</summary>
public sealed class SceneModelGeometry
{
    private readonly Mesh _mesh;
    private SceneModelGeometry(SceneModelGeometryKind kind, Mesh mesh, GrnAnimatedMesh? animation)
    { Kind = kind; _mesh = mesh; Animation = animation; }

    public SceneModelGeometryKind Kind { get; }
    public GrnAnimatedMesh? Animation { get; }
    public GrnPose? Pose => Animation?.Pose;
    public Mesh BindMesh => Animation?.BindMesh ?? _mesh;
    public static SceneModelGeometry ForMesh(Mesh mesh) => new(
        mesh.HasDynamicVertices ? SceneModelGeometryKind.CpuDeformed : SceneModelGeometryKind.Static, mesh, null);
    public static SceneModelGeometry ForCpuSkinning(GrnAnimatedMesh animation) => new(
        SceneModelGeometryKind.CpuDeformed, animation.BindMesh, animation);
    public static SceneModelGeometry ForGpuSkinning(GrnAnimatedMesh animation) => new(
        SceneModelGeometryKind.GpuSkinned, animation.BindMesh, animation);

    public Mesh GetCpuRenderMesh()
    {
        if (Kind == SceneModelGeometryKind.GpuSkinned)
            throw new InvalidOperationException("GPU-skinned geometry requires the skeletal draw path. Use MaterializeCpuMesh for an explicit CPU snapshot.");
        return Animation?.Mesh ?? _mesh;
    }

    /// <summary>Explicit on-demand CPU snapshot for tools, not an implicit draw-time fallback.</summary>
    public Mesh MaterializeCpuMesh() => Animation?.MaterializeCpuMesh() ?? _mesh;
    public bool TryGetRadialSweep(MeshSurface surface, out Vector4 parameters) => Animation is { } animation
        ? animation.TryGetRadialSweep(surface, out parameters)
        : MeshSurfaceRadialSweep.TryCalculate(_mesh, surface, out parameters);

    internal bool UsesCpuMesh(Mesh mesh) => Kind != SceneModelGeometryKind.GpuSkinned &&
        (Animation is { } animation
            ? animation.IsCpuMeshCurrent && ReferenceEquals(animation.Mesh, mesh)
            : ReferenceEquals(_mesh, mesh));
}
