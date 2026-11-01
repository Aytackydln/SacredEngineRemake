namespace Sacred.Granny.Animation;

/// <summary>Explicit CPU adapter. Recording a request elsewhere never marks CPU palettes current.</summary>
public sealed class GrnCpuPoseRequestAdapter : IGrnPoseRequestSink
{
    private readonly GrnAnimatedMesh _owner;
    private ulong _sourceVersion;
    private GrnAnimationClip? _animation;
    internal GrnCpuPoseRequestAdapter(GrnAnimatedMesh owner) => _owner = owner;
    public ulong EvaluatedSequence { get; private set; }
    private GrnAsyncPoseEvaluator? _async;
    /// <summary>Opt-in background sampling; explicit Evaluate calls remain synchronous.</summary>
    public bool Asynchronous { get; set; }
    public static int UpdatesPerSecond
    {
        get => GrnAsyncPoseEvaluator.UpdatesPerSecond;
        set => GrnAsyncPoseEvaluator.UpdatesPerSecond = value;
    }
    public void Submit(in GrnPoseRequest request)
    {
        if (!Asynchronous || !_owner.Pose.IsEvaluated) { Evaluate(request); return; }
        if (request.Instance != _owner.Playback.Instance)
            throw new InvalidOperationException("Pose request belongs to an expired or different instance.");
        EvaluatedSequence = (_async ??= new(_owner.BindMesh, _owner.Pose.Data, request.Animation)).Submit(_owner, request, EvaluatedSequence);
    }
    private GrnPoseEvaluator? _queryEvaluator;
    private GrnPoseRequest? _queryRequest;
    /// <summary>Query-only storage; never changes published palettes, revisions or CPU vertices.
    /// Valid until the next query on this adapter. Call from the owning thread.</summary>
    public GrnPose EvaluateSnapshot(in GrnPoseRequest request)
    {
        if (request.Instance != _owner.Playback.Instance)
            throw new InvalidOperationException("Pose request belongs to an expired or different instance.");
        _queryEvaluator ??= new(_owner.Pose.Data, request.Animation);
        if (_queryRequest == request) return _queryEvaluator.Pose;
        if (!ReferenceEquals(_queryEvaluator.Animation, request.Animation))
            _queryEvaluator.SetAnimation(request.Animation);
        var pose = request.Blend is { } blend
            ? _queryEvaluator.EvaluateBlended(request.TimeSeconds, request.TimeMode, blend)
            : request.TimeMode == GrnPoseTimeMode.Clamp
                ? _queryEvaluator.EvaluateClamped(request.TimeSeconds)
                : _queryEvaluator.Evaluate(request.TimeSeconds);
        _queryRequest = request;
        return pose;
    }

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
        var pose = request.Blend is { } blend ? _owner.Evaluator.EvaluateBlended(request.TimeSeconds, request.TimeMode, blend)
            : request.TimeMode == GrnPoseTimeMode.Clamp
            ? _owner.Evaluator.EvaluateClamped(request.TimeSeconds)
            : _owner.Evaluator.Evaluate(request.TimeSeconds);
        EvaluatedSequence = request.Sequence;
        if (request.MaterializeCpuVertices) _owner.MaterializeCpuMesh();
        return pose;
    }
}




