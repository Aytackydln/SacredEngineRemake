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

    public DoorMotionPlayback(Mesh mesh, GrnMeshSkin? skin, GrnAnimationClip? open, GrnAnimationClip? close)
    {
        _sourceMesh = mesh;
        Mesh = mesh;
        _skin = skin;
        _open = open;
        _close = close;
    }

    public Mesh Mesh { get; private set; }
    public bool CanReset => _skin is not null && _close is not null;

    public void SetInitialState(bool open)
    {
        _playing = false;
        if (open)
            SetState(true, applyImmediately: true);
        else
            Mesh = _sourceMesh;
    }

    public void SetState(bool open, bool applyImmediately = false)
    {
        var clip = open ? _open : _close;
        if (clip is null || _skin is null)
            return;

        _clip = clip;
        _animatedMesh ??= new GrnAnimatedMesh(_sourceMesh, _skin, clip);
        _animatedMesh.SetAnimation(clip);
        Mesh = _animatedMesh.Mesh;
        _timeSeconds = applyImmediately ? clip.DurationSeconds : 0.0f;
        _animatedMesh.ApplyClamped(_timeSeconds);
        _playing = !applyImmediately;
    }

    public void Update(float deltaSeconds)
    {
        if (!_playing || _clip is null || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;

        _timeSeconds = MathF.Min(_timeSeconds + deltaSeconds, _clip.DurationSeconds);
        _animatedMesh!.ApplyClamped(_timeSeconds);
        _playing = _timeSeconds < _clip.DurationSeconds;
    }
}
