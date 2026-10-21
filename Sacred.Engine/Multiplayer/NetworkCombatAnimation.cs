using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Animation;
using Sacred.GameplayHost.World;
using Sacred.Granny.Animation;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Explicit received attack motion and native 0x593320 rate/fade rules.
/// Motion loads are cached per rendered actor; cancellation/replacement invalidates late completions.</summary>
internal sealed class NetworkCombatAnimation(CharacterAnimationState animation,
    Func<uint, Task<GrnAnimationClip?>> loadMotion)
{
    private readonly Dictionary<(uint Motion, uint? Fallback), Task<GrnAnimationClip?>> _motions = [];
    private GoldCreatureAttackEvent? _attack;
    private GoldCreatureSpecialMoveEvent? _special;
    private GoldCreatureCastEvent? _cast;
    private GameplayCombatArtAnimation? _artAnimation;
    private uint _motion;
    private long _version = -1;
    private float _elapsed;
    private bool _playing, _finished, _needsStart, _selectionReported;
    public float GlobalPlaybackScale { get; set; } = 1;
    public string? MotionSource { get; init; }
    public Func<GoldReplicatedObject, GameplayCombatArtAnimation?>? SelectArtAnimation { get; set; }
    public Action<GoldReplicatedObject, float>? ArtTiming { get; set; }
    public bool Active => (_attack is not null || _artAnimation is not null) && !_finished;
    public (GoldCreatureAttackEvent Attack, GrnAnimationClip Clip, float SampleTime, float Age)? BowRelease { get; private set; }

    public void ContinueFrom(NetworkCombatAnimation previous)
    {
        _attack = previous._attack; _special = previous._special; _motion = previous._motion;
        _cast = previous._cast; _artAnimation = previous._artAnimation;
        _version = previous._version; _elapsed = previous._elapsed; _finished = previous._finished;
        _needsStart = true; BowRelease = previous.BowRelease;
        // Equipment/form rebuilding can change the model's motion archive. Retain
        // action time, but load clips through this presentation's own model.
        if (MotionSource == previous.MotionSource)
            foreach (var (motion, load) in previous._motions) _motions[motion] = load;
    }

    public void Update(GoldReplicatedObject actor, float seconds)
    {
        if (_version != actor.AttackVersion || !ReferenceEquals(_attack, actor.Attack) ||
            !ReferenceEquals(_special, actor.SpecialMove) || !ReferenceEquals(_cast, actor.Cast))
        {
            _version = actor.AttackVersion;
            if (actor.Attack is null && actor.SpecialMove is null && actor.Cast is null && _playing) animation.StopNpcAnimation();
            _attack = actor.Attack; _special = actor.SpecialMove; _elapsed = 0; _finished = false; _needsStart = true;
            _cast = actor.Cast;
            _artAnimation = null; _selectionReported = false;
            BowRelease = null;
            if (_attack is null && _special is null && _cast is null) { _playing = false; return; }
        }
        if (_attack is null && _special is null && _cast is null || _finished) return;
        if (_needsStart && _artAnimation is null)
        {
            _artAnimation = SelectArtAnimation is { } select ? select(actor) : _special is { } special
                ? GameplayCombatArtMotion.SelectSpecialMove(special.Code, actor.BaseState.ObjectType, 0) : null;
            var motion = _attack?.Motion ?? _artAnimation?.Motion;
            if (motion is null)
            {
                if (_playing) animation.StopNpcAnimation();
                _playing = false;
                if (!_selectionReported)
                    EngineLog.WriteLine($"Network combat art pending animation/rate: ref {actor.BaseState.ObjectReference}; spell {_cast?.Code}; special move {_special?.Code}.");
                _selectionReported = true;
                return;
            }
            _motion = motion.Value;
        }
        var key = (_motion, _artAnimation?.FallbackMotion);
        if (!_motions.TryGetValue(key, out var load)) _motions[key] = load = LoadArt(_motion, key.Item2);
        if (!load.IsCompleted) { _elapsed += MathF.Max(0, seconds); return; }
        var clip = load.GetAwaiter().GetResult();
        var actorFactor = actor.Creature!.AnimationSpeedMultiplier;
        var rate = (_attack?.Speed ?? _artAnimation?.Speed ?? 1) * actorFactor;
        if (clip is null || !float.IsFinite(actorFactor) || actorFactor <= 0 || !float.IsFinite(rate) || rate <= 0 ||
            !float.IsFinite(clip.PlaybackSpeedMultiplier) || clip.PlaybackSpeedMultiplier <= 0 ||
            !float.IsFinite(GlobalPlaybackScale) || GlobalPlaybackScale <= 0)
        {
            Finish();
            EngineLog.WriteLine($"Network attack pending unsupported motion/rate: ref {actor.BaseState.ObjectReference}; motion {_motion}; rate {rate}.");
            return;
        }
        // Gold 0x413693 applies Balance.bin's global motion scale after authored speed.
        // The blend denominator in 0x593320 excludes that global scale.
        var timing = new SacredNpcAnimationTiming(rate * clip.PlaybackSpeedMultiplier * GlobalPlaybackScale,
            MathF.Min(.2f, (_artAnimation?.BlendNumerator ?? .5f) / rate), actorFactor);
        if (_artAnimation is not null) ArtTiming?.Invoke(actor, clip.DurationSeconds / timing.PlaybackSpeed);
        var releaseTime = clip.DurationSeconds * GameplayBowRelease.Progress(clip.BowReleaseTailFraction);
        if (_attack is { IsBow: true } && float.IsFinite(releaseTime) && _elapsed * timing.PlaybackSpeed >= releaseTime)
            BowRelease = (_attack, clip, releaseTime, _elapsed - releaseTime / timing.PlaybackSpeed);
        if (_elapsed >= clip.DurationSeconds / timing.PlaybackSpeed) { Finish(); return; }
        if (_needsStart)
        {
            animation.PlayNpcAnimation(clip, timing, _elapsed * timing.PlaybackSpeed);
            _playing = true; _needsStart = false;
            EngineLog.WriteLine($"Network attack animation: ref {actor.BaseState.ObjectReference}; motion {_motion}; clip {clip.Name}; speed {timing.PlaybackSpeed:0.###}; blend {timing.BlendSeconds:0.###}s; special move {_special?.Code}; spell {_cast?.Code}.");
        }
        _elapsed += MathF.Max(0, seconds);
    }

    private async Task<GrnAnimationClip?> LoadArt(uint motion, uint? fallback)
    {
        var clip = await Load(motion).ConfigureAwait(false);
        return clip ?? (fallback is { } slot && slot != motion ? await Load(slot).ConfigureAwait(false) : null);
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
