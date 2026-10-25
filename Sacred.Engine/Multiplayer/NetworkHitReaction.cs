using System;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Animation;
using Sacred.Granny.Animation;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Damage reactions retain their receipt age while the model's AF motion loads.</summary>
internal sealed class NetworkHitReaction(CharacterAnimationState animation, Func<uint, Task<GrnAnimationClip?>> loadMotion)
{
    private Task<GrnAnimationClip?>? _clip;
    private float _age, _factor, _weight;
    private bool _pending;
    private uint _reference;
    public void Receive(GoldReplicatedObject actor, SacredCreatureClass creatureClass, float age)
    {
        _clip ??= loadMotion(SacredCreatureHitReaction.Motion);
        _reference = actor.BaseState.ObjectReference;
        _age = age; _factor = actor.Creature!.AnimationSpeedMultiplier;
        _weight = SacredCreatureHitReaction.Weight(creatureClass); _pending = true;
    }
    public void Update(float seconds, float globalScale)
    {
        if (!_pending) return;
        if (_clip?.IsCompleted != true) { _age += seconds; return; }
        _pending = false;
        GrnAnimationClip? clip;
        try { clip = _clip.GetAwaiter().GetResult(); }
        catch (Exception error) { EngineLog.WriteLine($"Network hit reaction could not load: {error.Message}."); return; }
        var speed = SacredCreatureHitReaction.Speed * _factor * (clip?.PlaybackSpeedMultiplier ?? 1) * globalScale;
        if (clip is null || !float.IsFinite(speed) || speed <= 0 || _age * speed >= clip.DurationSeconds) return;
        animation.PlayHitReaction(clip, speed, _weight, _age * speed);
        EngineLog.WriteLine($"Network hit reaction: ref {_reference}; clip {clip.Name}; speed {speed}; control weight {_weight}.");
    }
}
