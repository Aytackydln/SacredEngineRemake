using System.Numerics;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Animation;

/// <summary>Compatibility CPU mesh plus an independent pose evaluator and explicit materialization.</summary>
public sealed class GrnAnimatedMesh
{
    private readonly Mesh _cpuMesh;
    private ulong _materializedVertexRevision;

    public GrnAnimatedMesh(Mesh sourceMesh, GrnMeshSkin skin, GrnAnimationClip animation)
        : this(sourceMesh, skin.SkinningData, animation) { }

    public GrnAnimatedMesh(Mesh sourceMesh, GrnSkinningData data, GrnAnimationClip animation)
    {
        if (sourceMesh.Vertices.Length != data.Vertices.Length)
            throw new ArgumentException("The skin vertex count does not match the source mesh.", nameof(data));
        BindMesh = sourceMesh;
        _cpuMesh = sourceMesh.CreateInstance();
        Evaluator = new GrnPoseEvaluator(data, animation);
    }

    public Mesh BindMesh { get; }
    public GrnPoseEvaluator Evaluator { get; }
    public GrnPose Pose => Evaluator.Pose;
    public GrnAnimationClip Animation => Evaluator.Animation;
    public ulong CpuPoseRevision { get; private set; }
    private bool _materializeCpuVertices = true;
    /// <summary>Playback policy. Enabling prepares the current pose, including held endpoints.</summary>
    public bool MaterializeCpuVertices
    {
        get => _materializeCpuVertices;
        set
        {
            if (value && Pose.IsEvaluated && !IsCpuMeshCurrent) MaterializeCpuMesh();
            _materializeCpuVertices = value;
        }
    }
    private GrnPosedSurfaceQueries? _surfaceQueries;
    public bool TryGetRadialSweep(MeshSurface surface, out Vector4 parameters) =>
        (_surfaceQueries ??= new(this)).TryGetRadialSweep(surface, out parameters);
    public bool IsCpuMeshCurrent => Pose.IsEvaluated && CpuPoseRevision == Pose.Revision && _materializedVertexRevision == _cpuMesh.VertexRevision;
    /// <summary>Current CPU vertices. Pose-only callers must explicitly materialize before accessing them.</summary>
    public Mesh Mesh => IsCpuMeshCurrent ? _cpuMesh : throw new InvalidOperationException(
        "CPU vertices are stale for the evaluated pose. Call MaterializeCpuMesh explicitly.");

    public void SetAnimation(GrnAnimationClip animation) => Evaluator.SetAnimation(animation);
    public GrnPose EvaluatePose(float timeSeconds) => Evaluator.Evaluate(timeSeconds);
    public GrnPose EvaluatePoseClamped(float timeSeconds) => Evaluator.EvaluateClamped(timeSeconds);

    public Mesh MaterializeCpuMesh()
    {
        if (IsCpuMeshCurrent) return _cpuMesh;
        GrnCpuSkinner.Materialize(Pose, _cpuMesh);
        CpuPoseRevision = Pose.Revision;
        _materializedVertexRevision = _cpuMesh.VertexRevision;
        return _cpuMesh;
    }

    public void Apply(float timeSeconds) { EvaluatePose(timeSeconds); MaterializeCpuMesh(); }
    public void ApplyClamped(float timeSeconds) { EvaluatePoseClamped(timeSeconds); MaterializeCpuMesh(); }
    public VertexPositionNormalTexture TransformVertex(int index) => GrnCpuSkinner.TransformVertex(Pose, index, BindMesh.Vertices[index]);
    public Vector3 TransformPosition(int index) => GrnCpuSkinner.TransformPosition(Pose, index);
    public bool TryTransformRigidPoint(string boneName, Vector3 bind, out Vector3 result) =>
        GrnPoseQueries.TryTransformRigidPoint(Pose, boneName, bind, out result);
    public bool TryTransformRigidDirection(string boneName, Vector3 bind, out Vector3 result) =>
        GrnPoseQueries.TryTransformRigidDirection(Pose, boneName, bind, out result);
}
