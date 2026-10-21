using System.Collections.ObjectModel;
using Sacred.Network.Gameplay;
using Sacred.Particles;

namespace Sacred.GameplayHost.World;

/// <summary>Local FX created by the native combat-art action, independently of its animation.</summary>
public sealed class GameplayCombatArtEffects
{
    private readonly GoldWorldReplica _world;
    private readonly Action<string> _log;
    private readonly Dictionary<long, GameplayCombatArtEffect> _entries = [];
    private readonly Dictionary<long, GoldCombatNotification> _pendingCasts = [];
    private readonly SacredParticleCatalogue _catalogue = SacredParticleCatalogue.LoadEmbedded(SacredParticleQuality.High);
    private long _received;
    private long _generation;
    public GameplayCombatArtEffects(GoldWorldReplica world, Action<string> log)
    {
        (_world, _log) = (world, log);
        Entries = new ReadOnlyDictionary<long, GameplayCombatArtEffect>(_entries);
    }
    public IReadOnlyDictionary<long, GameplayCombatArtEffect> Entries { get; }
    public Func<GoldReplicatedObject, uint, short?>? CastSpeedResolver { get; set; }
    public Func<GoldReplicatedObject, bool>? DaemonTargetEligible { get; set; }

    public void Advance(float seconds)
    {
        foreach (var (id, effect) in _entries.ToArray())
        {
            effect.Age += MathF.Max(0, seconds);
            if (effect.Age >= effect.MaximumAge || !_world.Objects.TryGetValue(effect.Owner, out var owner) ||
                owner.InstanceVersion != effect.OwnerInstance ||
                (owner.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
                _entries.Remove(id);
        }
        foreach (var notification in _world.Combat.Recent)
        {
            if (notification.Version <= _received) continue;
            _received = notification.Version;
            if (!_world.Objects.TryGetValue(notification.Event.ObjectReference, out var owner) ||
                owner.InstanceVersion != notification.InstanceVersion ||
                (owner.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) continue;
            // EiSeraphim event 67; EiMagic event 23. Attachments 21/22 are native bone names.
            if (notification.Event is GoldCreatureSpecialMoveEvent { Code: 1018 } && owner.BaseState.ObjectType is 1 or 0x8D)
                Add(owner, 844, 1018, "Bip01 R Finger3", 1);
            else if (notification.Event is GoldCreatureSpecialMoveEvent { Code: 1012 or 1015 } gladiator &&
                     owner.BaseState.ObjectType == 2)
                // Gold 0x50359C/0x503758: event 65, independent of attack/casting speed.
                Add(owner, 803, gladiator.Code, "Bip01 R Finger3", 1);
            else if (notification.Event is GoldCreatureSpecialMoveEvent daemon && GameplayDaemonTargetedArt.Handles(owner.BaseState.ObjectType, daemon.Code) &&
                     _world.Objects.TryGetValue(daemon.TargetReference, out var target) && DaemonTargetEligible?.Invoke(target) == true)
            {
                foreach (var attachment in GameplayDaemonTargetedArt.Attachments(daemon.Code))
                    Add(owner, 791, attachment.Preset, attachment.Bone, 1);
            }
            else if (notification.Event is GoldCreatureCastEvent cast)
            {
                var attachments = GameplayCastAttachments.Select(cast.Code);
                if (attachments.IsEmpty) continue;
                if (CastSpeedResolver?.Invoke(owner, cast.Code) is not (> 0 and var speed))
                {
                    _log($"Network casting FX pending speed: owner {cast.ObjectReference}; spell {cast.Code}.");
                    _pendingCasts[notification.Version] = notification;
                    continue;
                }
                var clock = speed * .01f;
                foreach (var attachment in attachments)
                    Add(owner, 791, attachment.Preset, attachment.Bone, clock);
            }
        }
        foreach (var (version, notification) in _pendingCasts.ToArray())
        {
            if (!_world.Objects.TryGetValue(notification.Event.ObjectReference, out var owner) ||
                owner.InstanceVersion != notification.InstanceVersion ||
                (owner.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 ||
                !ReferenceEquals(owner.Cast?.Message, notification.Event.Message))
            {
                _pendingCasts.Remove(version);
                continue;
            }
            var cast = (GoldCreatureCastEvent)notification.Event;
            if (CastSpeedResolver?.Invoke(owner, cast.Code) is not (> 0 and var speed)) continue;
            foreach (var attachment in GameplayCastAttachments.Select(cast.Code))
                Add(owner, 791, attachment.Preset, attachment.Bone, speed * .01f);
            _pendingCasts.Remove(version);
        }
    }
    private void Add(GoldReplicatedObject owner, uint type, int preset, string bone, float clock)
    {
        if (!_catalogue.TryGetEventDefinition(type, preset, out var definition) || definition is null) return;
        var drain = definition.ParameterSets.Max(set => MathF.Min(
            set.Motion.FadeChangeRate < 0 ? 255 / -set.Motion.FadeChangeRate : float.PositiveInfinity,
            set.Motion.SizeChangeRate < 0 ? (set.Emission.Size + MathF.Abs(set.Emission.SizeRandomWidth)) / -set.Motion.SizeChangeRate : float.PositiveInfinity));
        if (definition.ParameterSets.All(set => set.Emission.EmissionInterval <= 0 && set.Emission.BurstCount == 0)) drain = 0;
        var maximumAge = (definition.EmissionDurationSeconds ?? 0) / clock + drain + .2f;
        var id = ++_generation;
        _entries[id] = new(id, owner.BaseState.ObjectReference, owner.InstanceVersion, type, preset, bone, maximumAge, clock);
        _log($"Network combat-art FX created: owner {owner.BaseState.ObjectReference}; resource {type}; preset {preset}; attachment {bone}; clock {clock:F2}.");
    }
    public void Clear() { _entries.Clear(); _pendingCasts.Clear(); _received = _world.Combat.Version; }
}

public sealed record GameplayCombatArtEffect(long Generation, uint Owner, long OwnerInstance, uint Type,
    int Preset, string Bone, float MaximumAge, float ClockScale = 1)
{
    public float Age { get; internal set; }
}
