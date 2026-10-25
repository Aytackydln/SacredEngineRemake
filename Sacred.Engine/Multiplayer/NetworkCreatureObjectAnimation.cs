using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Animation;
using Sacred.Granny.Animation;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>170's cCreature::CreateAnimation route, separate from bow/melee attack packets.</summary>
internal sealed class NetworkCreatureObjectAnimation(CharacterAnimationState animation,
    Func<uint, Task<GrnAnimationClip?>> loadMotion)
{
    private readonly Dictionary<uint, Task<GrnAnimationClip?>> _clips = [];
    private GoldObjectAnimationEvent? _request;
    private float _age;
    private bool _playing;
    private int _cycle = -1;
    private Func<bool>? _complete;
    private long _movementVersion, _attackVersion;

    public bool Active => _request is not null;

    public void Receive(GoldObjectAnimationEvent request, GoldReplicatedObject actor, float age, Func<bool> complete)
    {
        Stop();
        _request = request; _age = age; _cycle = -1; _complete = complete;
        _movementVersion = actor.MovementVersion; _attackVersion = actor.AttackVersion;
    }

    public void Update(GoldReplicatedObject actor, float seconds, float globalScale)
    {
        if (_request is not { } request) return;
        if (_movementVersion != actor.MovementVersion || _attackVersion != actor.AttackVersion)
        { Stop(); return; }
        if (!_clips.TryGetValue(request.Animation, out var load))
            _clips[request.Animation] = load = loadMotion(request.Animation);
        if (!load.IsCompleted) { _age += seconds; return; }
        GrnAnimationClip? clip;
        try { clip = load.GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            EngineLog.WriteLine($"Network creature motion {request.Animation} could not load: {error.Message}.");
            Stop(); return;
        }
        var repetitions = BitConverter.UInt32BitsToSingle(request.FirstArgumentBits);
        var factor = actor.Creature!.AnimationSpeedMultiplier;
        var rate = BitConverter.UInt32BitsToSingle(request.SecondArgumentBits) * factor *
            (clip?.PlaybackSpeedMultiplier ?? 1) * globalScale;
        if (clip is null || clip.DurationSeconds <= 0 || !float.IsFinite(rate) || rate <= 0 || !float.IsFinite(repetitions) || repetitions <= 0)
        {
            EngineLog.WriteLine($"Network creature animation unavailable: ref {actor.BaseState.ObjectReference}; motion {request.Animation}; rate {rate}.");
            Stop(); return;
        }
        var clock = _age * rate;
        if (clock >= clip.DurationSeconds * repetitions) { Stop(); return; }
        var cycle = (int)(clock / clip.DurationSeconds);
        if (!_playing || cycle != _cycle)
        {
            if (!_playing && _complete?.Invoke() != true) { Stop(); return; }
            animation.PlayNpcAnimation(clip, new SacredNpcAnimationTiming(rate, 0, factor), clock % clip.DurationSeconds);
            if (!_playing) EngineLog.WriteLine($"Network creature object animation: ref {actor.BaseState.ObjectReference}; motion {request.Animation}; clip {clip.Name}; rate {rate}.");
            _playing = true;
            _cycle = cycle;
        }
        _age += seconds;
    }

    private void Stop() { if (_playing) animation.StopNpcAnimation(); _request = null; _complete = null; _playing = false; }
}
