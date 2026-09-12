using System;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Plays native door sequences through the Granny hierarchy, with a rigid-track fallback.</summary>
internal sealed class DoorMotionPlayback
{
    private readonly SceneModel _model;
    private readonly GrnMeshSkin? _skin;
    private readonly GrnAnimationClip? _open;
    private readonly GrnAnimationClip? _close;
    private GrnAnimatedMesh? _animatedMesh;
    private GrnAnimationClip? _clip;
    private GrnTransformTrack? _rigidTrack;
    private Vector3 _rigidStart;
    private Vector3[] _poseStartPositions = [];
    private bool _usesSkinnedMotion;
    private float _timeSeconds;
    private bool _playing;

    public DoorMotionPlayback(SceneModel model, GrnMeshSkin? skin, GrnAnimationClip? open, GrnAnimationClip? close)
    {
        _model = model;
        _skin = skin;
        _open = open;
        _close = close;
    }

    /// <summary>True when the asset contains a native deactivation sequence.</summary>
    public bool CanReset => _close is not null;

    public void SetInitialState(bool open)
    {
        if (open)
            SetState(open, applyImmediately: true);
    }

    public void SetState(bool open, bool applyImmediately = false)
    {
        _clip = open ? _open : _close;
        if (_clip is null)
            return;

        _rigidTrack = FindAnimatedTrack(_clip);
        _rigidStart = _rigidTrack is null ? Vector3.Zero : Sample(_rigidTrack.TranslationTimes, _rigidTrack.Translations, 0.0f);
        _usesSkinnedMotion = TryCreateSkinnedMotion(_clip);
        _timeSeconds = applyImmediately ? EndTime(_clip) : 0.0f;
        ApplyPose();
        _playing = !applyImmediately;
    }

    public void Update(float deltaSeconds)
    {
        if (!_playing || _clip is null)
            return;

        _timeSeconds = MathF.Min(_timeSeconds + deltaSeconds, EndTime(_clip));
        ApplyPose();
        _playing = _timeSeconds < EndTime(_clip);
    }

    private bool TryCreateSkinnedMotion(GrnAnimationClip clip)
    {
        if (_skin is null)
            return false;

        _animatedMesh ??= new GrnAnimatedMesh(_model.Mesh, _skin, clip);
        _animatedMesh.SetAnimation(clip);
        _animatedMesh.Apply(0.0f);
        if (_poseStartPositions.Length != _animatedMesh.Mesh.Vertices.Length)
            _poseStartPositions = new Vector3[_animatedMesh.Mesh.Vertices.Length];
        for (var index = 0; index < _poseStartPositions.Length; index++)
            _poseStartPositions[index] = _animatedMesh.Mesh.Vertices[index].Position;
        _animatedMesh.Apply(EndTime(clip));
        return _animatedMesh.Mesh.Vertices
            .Select((vertex, index) => Vector3.DistanceSquared(vertex.Position, _poseStartPositions[index]))
            .Any(distanceSquared => distanceSquared > 0.000001f);
    }

    private void ApplyPose()
    {
        if (_usesSkinnedMotion)
        {
            // A previous fallback must never survive into a skinned sequence.
            _model.SetModelOffset(Vector3.Zero);
            _animatedMesh!.Apply(_timeSeconds);
            _model.SetMesh(_animatedMesh.Mesh);
            return;
        }

        if (_rigidTrack is null)
            return;

        // Several fixed world props omit vertex bone weights even though their GRN sequence
        // has a moving dummy. The sequence translation remains authored local-model data.
        var translation = Sample(_rigidTrack.TranslationTimes, _rigidTrack.Translations, _timeSeconds);
        _model.SetModelOffset((translation - _rigidStart) * _model.Scale);
    }

    private static GrnTransformTrack? FindAnimatedTrack(GrnAnimationClip clip) =>
        clip.Tracks.LastOrDefault(track => track is not null &&
            track.Translations.Length > 1 &&
            track.Translations.Any(value => Vector3.DistanceSquared(value, track.Translations[0]) > 0.000001f));

    private static Vector3 Sample(float[] times, Vector3[] values, float time)
    {
        if (values.Length == 0)
            return Vector3.Zero;
        if (values.Length == 1 || time <= times[0])
            return values[0];
        var last = Math.Min(times.Length, values.Length) - 1;
        if (time >= times[last])
            return values[last];
        for (var index = 1; index <= last; index++)
        {
            if (time > times[index])
                continue;
            var duration = times[index] - times[index - 1];
            var amount = duration > float.Epsilon ? (time - times[index - 1]) / duration : 0.0f;
            return Vector3.Lerp(values[index - 1], values[index], amount);
        }
        return values[last];
    }

    private static float EndTime(GrnAnimationClip clip) => MathF.Max(0.0f, clip.DurationSeconds - 0.0001f);
}
