using System;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Animation;

/// <summary>An independent one-shot Granny control blended with the main motion's weight of one.</summary>
internal sealed class CharacterAnimationOverlay
{
    private GrnAnimationClip? _clip;
    private float _time, _speed, _weight;
    public bool Active => _clip is not null;
    public void Clear() => _clip = null;
    public void Play(GrnAnimationClip clip, float speed, float weight, float time)
    {
        _clip = time < clip.DurationSeconds ? clip : null;
        _time = time; _speed = speed; _weight = weight;
    }
    public void Advance(float seconds)
    {
        _time += seconds * _speed;
        if (_clip is { } clip && _time >= clip.DurationSeconds) _clip = null;
    }
    public GrnPoseBlend? Blend => _clip is { } clip
        ? new(clip, MathF.Max(0, _time), 1 / (1 + _weight)) { SourceMode = GrnPoseTimeMode.Clamp } : null;
}
