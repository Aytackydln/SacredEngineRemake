using System;
using System.Threading.Tasks;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Animation;
using Sacred.Granny.Animation;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>One clamped death action; its terminal pose stays until removal or an authoritative revival.</summary>
internal sealed class NetworkDeathAnimation
{
    private readonly CharacterAnimationState _animation;
    private readonly Task<(uint Slot, GrnAnimationClip? Clip)> _load;
    private readonly uint _reference;
    private readonly float _factor;
    private readonly bool _corpse;
    private readonly uint _slot;
    private float _age;
    private bool _started, _failed;
    public bool PoseReady => _started;

    public NetworkDeathAnimation(CharacterAnimationState animation, GoldReplicatedObject actor, float age,
        bool corpse, Func<uint, Task<GrnAnimationClip?>> loadMotion, Func<uint, bool> available)
    {
        (_animation, _reference, _factor, _age, _corpse) =
            (animation, actor.BaseState.ObjectReference, actor.Creature!.AnimationSpeedMultiplier, age, corpse);
        // Gold 53F2DA..53F31D applies a second special-death roll to this native creature group.
        _slot = SacredCreatureDeathMotion.Select(actor.Physical?.Word64 ?? 0, available,
            () => Random.Shared.Next(32768), actor.BaseState.ObjectType is >= 0x8C and <= 0x92);
        _load = LoadAsync(loadMotion);
    }

    private async Task<(uint Slot, GrnAnimationClip? Clip)> LoadAsync(Func<uint, Task<GrnAnimationClip?>> load) =>
        (_slot, await load(_slot).ConfigureAwait(false));

    public void Update(float seconds, float globalScale)
    {
        if (!_started && !_failed && _load.IsCompleted)
        {
            try
            {
                var (slot, clip) = _load.GetAwaiter().GetResult();
                var rate = _factor * (clip?.PlaybackSpeedMultiplier ?? 1) * globalScale;
                if (clip is null || !float.IsFinite(rate) || rate <= 0)
                {
                    EngineLog.WriteLine($"Network death motion unavailable: ref {_reference}; slot {slot:X2}; rate {rate}.");
                    _failed = true;
                }
                else
                {
                    _animation.PlayNpcAnimation(clip, new SacredNpcAnimationTiming(rate, 0, _factor),
                        _corpse ? clip.DurationSeconds : MathF.Min(_age * rate, clip.DurationSeconds));
                    _started = true;
                    EngineLog.WriteLine($"Network death animation: ref {_reference}; slot {slot:X2}; clip {clip.Name}; rate {rate}; elapsed {_age:0.###}; corpse {_corpse}; terminal pose held.");
                }
            }
            catch (Exception error)
            { _failed = true; EngineLog.WriteLine($"Network death animation failed: ref {_reference}; {error.Message}."); }
        }
        _age += seconds;
    }
}
