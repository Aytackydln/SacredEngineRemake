namespace Sacred.Granny.Animation;

/// <summary>Explicit CPU adapter. Recording a request elsewhere never marks CPU palettes current.</summary>
public sealed class GrnCpuPoseRequestAdapter : IGrnPoseRequestSink
{
    private readonly GrnAnimatedMesh _owner;
    private ulong _sourceVersion;
    private GrnAnimationClip? _animation;
    internal GrnCpuPoseRequestAdapter(GrnAnimatedMesh owner) => _owner = owner;
    public ulong EvaluatedSequence { get; private set; }
    public void Submit(in GrnPoseRequest request) => Evaluate(request);
    public GrnPose Evaluate(in GrnPoseRequest request)
    {
        if (request.Instance != _owner.Playback.Instance)
            throw new InvalidOperationException("Pose request belongs to an expired or different instance.");
        if (_sourceVersion != request.SourceVersion || !ReferenceEquals(_animation, request.Animation))
        {
            _owner.Evaluator.SetAnimation(request.Animation);
            _animation = request.Animation;
            _sourceVersion = request.SourceVersion;
        }
        var pose = request.TimeMode == GrnPoseTimeMode.Clamp
            ? _owner.Evaluator.EvaluateClamped(request.TimeSeconds)
            : _owner.Evaluator.Evaluate(request.TimeSeconds);
        EvaluatedSequence = request.Sequence;
        if (request.MaterializeCpuVertices) _owner.MaterializeCpuMesh();
        return pose;
    }
}
