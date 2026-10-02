using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.World.Objects;

/// <summary>Plays the asset's activation/deactivation clips once and holds their final pose.</summary>
public sealed class DoorMotionPlayback
{
    private readonly Mesh _sourceMesh;
    private readonly GrnMeshSkin? _skin;
    private readonly GrnAnimationClip? _open;
    private readonly GrnAnimationClip? _close;
    private GrnAnimatedMesh? _animatedMesh;
    private GrnAnimationClip? _clip;
    private float _timeSeconds;
    private bool _playing;
    private bool _usesAnimation;

    public DoorMotionPlayback(Mesh mesh, GrnMeshSkin? skin, GrnAnimationClip? open, GrnAnimationClip? close)
    {
        _sourceMesh = mesh;
        _skin = skin;
        _open = open;
        _close = close;
    }

    public Mesh Mesh => _usesAnimation ? _animatedMesh!.Mesh : _sourceMesh;
    public GrnAnimatedMesh? AnimatedMesh => _usesAnimation ? _animatedMesh : null;
    public GrnPose? Pose => AnimatedMesh?.Pose;
    private bool _materializeCpuVertices = true;
    public bool MaterializeCpuVertices
    {
        get => _animatedMesh?.MaterializeCpuVertices ?? _materializeCpuVertices;
        set
        {
            if (value && _usesAnimation && _animatedMesh!.Pose.IsEvaluated) _animatedMesh.MaterializeCpuMesh();
            _materializeCpuVertices = value;
            if (_animatedMesh is not null) _animatedMesh.MaterializeCpuVertices = value;
        }
    }
    public bool CanReset => _skin is not null && _close is not null;

    public void SetInitialState(bool open)
    {
        _playing = false;
        if (open)
            SetState(true, applyImmediately: true);
        else
            _usesAnimation = false;
    }

    public void SetState(bool open, bool applyImmediately = false)
    {
        var clip = open ? _open : _close;
        if (clip is null || _skin is null)
            return;

        _clip = clip;
        _animatedMesh ??= new GrnAnimatedMesh(_sourceMesh, _skin, clip);
        if (!_materializeCpuVertices) _animatedMesh.MaterializeCpuVertices = false;
        _animatedMesh.SetAnimation(clip);
        _usesAnimation = true;
        _timeSeconds = applyImmediately ? clip.DurationSeconds : 0.0f;
        ApplyCurrentPose();
        _playing = !applyImmediately;
    }

    public void Update(float deltaSeconds)
    {
        if (!_playing || _clip is null || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;

        _timeSeconds = MathF.Min(_timeSeconds + deltaSeconds, _clip.DurationSeconds);
        ApplyCurrentPose();
        _playing = _timeSeconds < _clip.DurationSeconds;
    }

    private void ApplyCurrentPose()
    {
        _animatedMesh!.EvaluatePoseClamped(_timeSeconds);
        if (MaterializeCpuVertices) _animatedMesh.MaterializeCpuMesh();
    }
}
