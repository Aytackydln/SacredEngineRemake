using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Animation;
using Sacred.Granny.Animation;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Explicit received attack motion and native 0x593320 rate/fade rules.
/// Motion loads are cached per rendered actor; cancellation/replacement invalidates late completions.</summary>
internal sealed class NetworkCombatAnimation(CharacterAnimationState animation,
    Func<uint, Task<GrnAnimationClip?>> loadMotion)
{
    private readonly Dictionary<uint, Task<GrnAnimationClip?>> _motions = [];
    private GoldCreatureAttackEvent? _attack;
    private long _version = -1;
    private float _elapsed;
    private bool _playing, _finished, _needsStart;
    public bool Active => _attack is not null && !_finished;

    public void Update(GoldReplicatedObject actor, float seconds)
    {
        if (_version != actor.AttackVersion || !ReferenceEquals(_attack, actor.Attack))
        {
            _version = actor.AttackVersion;
            if (actor.Attack is null && _playing) animation.StopNpcAnimation();
            _attack = actor.Attack; _elapsed = 0; _finished = false; _needsStart = true;
            if (_attack is null) { _playing = false; return; }
            if (!_motions.ContainsKey(_attack.Motion)) _motions[_attack.Motion] = Load(_attack.Motion);
        }
        if (_attack is null || _finished) return;
        var load = _motions[_attack.Motion];
        if (!load.IsCompleted) { _elapsed += MathF.Max(0, seconds); return; }
        var clip = load.GetAwaiter().GetResult();
        var actorFactor = actor.Creature!.AnimationSpeedMultiplier;
        var rate = _attack.Speed * actorFactor;
        if (clip is null || !float.IsFinite(actorFactor) || actorFactor <= 0 || !float.IsFinite(rate) || rate <= 0 ||
            !float.IsFinite(clip.PlaybackSpeedMultiplier) || clip.PlaybackSpeedMultiplier <= 0)
        {
            Finish();
            EngineLog.WriteLine($"Network attack pending unsupported motion/rate: ref {actor.BaseState.ObjectReference}; motion {_attack.Motion}; rate {rate}.");
            return;
        }
        var timing = new SacredNpcAnimationTiming(rate * clip.PlaybackSpeedMultiplier, MathF.Min(.2f, .5f / rate), actorFactor);
        if (_elapsed >= clip.DurationSeconds / timing.PlaybackSpeed) { Finish(); return; }
        if (_needsStart)
        {
            animation.PlayNpcAnimation(clip, timing, _elapsed * timing.PlaybackSpeed);
            _playing = true; _needsStart = false;
            EngineLog.WriteLine($"Network attack animation: ref {actor.BaseState.ObjectReference}; motion {_attack.Motion}; clip {clip.Name}; speed {timing.PlaybackSpeed:0.###}; blend {timing.BlendSeconds:0.###}s.");
        }
        _elapsed += MathF.Max(0, seconds);
    }

    private async Task<GrnAnimationClip?> Load(uint motion)
    {
        try { return await loadMotion(motion).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        catch (Exception error)
        {
            EngineLog.WriteLine($"Network combat motion {motion} could not load: {error.Message}.");
            return null;
        }
    }

    private void Finish()
    {
        if (_playing) animation.StopNpcAnimation();
        _playing = false; _finished = true;
    }
}
