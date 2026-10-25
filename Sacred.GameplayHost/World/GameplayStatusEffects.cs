using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Sacred.Core.CombatArts;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Particles;

namespace Sacred.GameplayHost.World;

/// <summary>CA release, refresh, lifetime and owner cleanup, independent of renderer and input.
/// Model archive duration is an input, just as it is for the native action state machine.</summary>
public sealed class GameplayStatusEffects
{
    private readonly GoldWorldReplica _world;
    private readonly Action<string> _log;
    private readonly Dictionary<long, GameplayStatusEffect> _effects = [];
    private readonly Dictionary<uint, PendingAction> _actions = [];
    private readonly SacredParticleCatalogue _particles = SacredParticleCatalogue.LoadEmbedded();
    private long _generation;
    private readonly GoldConsumerCursor _cursor;

    public GameplayStatusEffects(GoldWorldReplica world, Action<string> log)
    {
        (_world, _log) = (world, log);
        _cursor = world.Consumers.CreateCursor("status-fx", work => work.Value is GoldCombatNotification);
        Entries = new ReadOnlyDictionary<long, GameplayStatusEffect>(_effects);
    }
    public IReadOnlyDictionary<long, GameplayStatusEffect> Entries { get; }
    public int PendingActions => _actions.Count;
    public Func<SacredParticleDefinition, float?>? VisualLifetime { get; set; }

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var (id, entry) in _effects.ToArray())
        {
            var effect = entry;
            if (!float.IsFinite(effect.Duration) && _particles.TryGetEventDefinition(effect.Type, effect.Preset, out var definition) &&
                VisualLifetime?.Invoke(definition) is { } duration)
                _effects[id] = effect = effect with { Duration = duration };
            effect.Age += seconds;
            // Once attached, native 0x5AA480 follows the wearer. Source validity gates
            // action release/owner commands, rather than deleting a surviving target's aura.
            if (effect.Age > effect.Duration || effect.GroundTarget is null && !Alive(effect.Owner, effect.OwnerInstance))
            {
                _effects.Remove(id);
                _log($"Status FX retired: owner {effect.Owner}; art {effect.Definition.Code}; generation {id}.");
            }
        }
        while (_world.Consumers.TryPeek(_cursor, out var work))
        {
            var notification = (GoldCombatNotification)work.Value;
            _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "status action delivery");
            var (kind, code, target) = notification.Event switch
            {
                GoldCreatureCastEvent cast => (SacredCombatArtKind.Spell, cast.Code, cast.TargetReference),
                GoldCreatureSpecialMoveEvent special => (SacredCombatArtKind.SpecialMove, special.Code, special.TargetReference),
                _ => (default, 0u, 0u)
            };
            if (code == 0 || !SacredCombatVisualCatalogue.Find(kind, code).Any() ||
               !Alive(notification.Event.ObjectReference, notification.InstanceVersion)) continue;
            var targetInstance = _world.Objects.TryGetValue(target, out var destination) ? destination.InstanceVersion : (long?)null;
            _actions[notification.Event.ObjectReference] = new(notification, kind, code, target, targetInstance);
            _log($"Status FX action queued: caster {notification.Event.ObjectReference}; {kind} {code}; awaiting native motion duration.");
        }
        foreach (var (caster, action) in _actions.ToArray())
        {
            if (!Alive(caster, action.Notification.InstanceVersion) || !_world.Objects.TryGetValue(caster, out var actor) ||
               !ReferenceEquals(action.Notification.Event.Message, actor.Cast?.Message ?? actor.SpecialMove?.Message))
            {
                _actions.Remove(caster);
                continue;
            }
            action.Age += seconds;
            if (action.AnimationSeconds is not { } duration) continue;
            var definitions = SacredCombatVisualCatalogue.Find(action.Kind, action.Code).ToArray();
            for (var channel = 0; channel < definitions.Length; channel++)
                if (!action.Released.Contains(channel) && action.Age >= duration * definitions[channel].ReleaseProgress)
                {
                    action.Released.Add(channel);
                    var definition = definitions[channel];
                    if (definition.Bone.Length == 0 || definition.TargetIsCaster ||
                        action.TargetInstance is { } instance && Alive(action.Target, instance) ||
                        definition.FallbackToCaster && !_world.Objects.ContainsKey(action.Target))
                        Add(actor, action.Target, definition, channel, Rank(actor, action.Code),
                            action.Age - duration * definition.ReleaseProgress, GroundTarget(action.Notification.Event));
                }
            if (action.Released.Count == definitions.Length) _actions.Remove(caster);
        }
    }

    /// <summary>Supplies archive clip time divided by actual native playback rate; repeat calls do not reset time.</summary>
    public void SetActionTiming(GoldReplicatedObject actor, float animationSeconds)
    {
        if (float.IsFinite(animationSeconds) && animationSeconds > 0 &&
           _actions.TryGetValue(actor.BaseState.ObjectReference, out var action) &&
           ReferenceEquals(action.Notification.Event.Message, actor.Cast?.Message ?? actor.SpecialMove?.Message))
            action.AnimationSeconds = animationSeconds;
    }

    /// <summary>Console/offline harness entry, using the same extracted factory definitions and lifecycle.</summary>
    public bool Apply(uint caster, uint target, SacredCombatArtKind kind, uint code, int rank = 1, GoldObjectPosition? groundTarget = null)
    {
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        if (!_world.Objects.TryGetValue(caster, out var actor) || !Alive(caster, actor.InstanceVersion)) return false;
        var definitions = SacredCombatVisualCatalogue.Find(kind, code).ToArray();
        var before = _generation;
        for (var i = 0; i < definitions.Length; i++) Add(actor, target, definitions[i], i, rank, 0, groundTarget);
        return _generation != before;
    }

    private void Add(GoldReplicatedObject caster, uint target, SacredCombatVisualDefinition definition, int channel, int rank, float age, GoldObjectPosition? groundTarget)
    {
        var ground = definition.Bone.Length == 0;
        if (ground && groundTarget is null) return;
        var useCaster = definition.TargetIsCaster || definition.FallbackToCaster && !_world.Objects.ContainsKey(target);
        var owner = ground ? 0u : useCaster ? caster.BaseState.ObjectReference : target;
        GoldReplicatedObject? wearer = null;
        if (!ground && (!_world.Objects.TryGetValue(owner, out wearer) || !Alive(owner, wearer.InstanceVersion))) return;
        string? pending = definition.Diagnostic;
        _particles.TryGetEventDefinition(definition.Resource, 0, out var particle);
        var visualAvailable = particle is not null && WorldAttachedParticleEffects.SupportsSurface(particle);
        if (!visualAvailable)
            pending ??= $"Visual family {particle?.NativeClass ?? definition.Resource.ToString()} is pending.";
        else if (particle?.StrikeSymbol is { FinishNotification: not 0 } strike)
            pending ??= $"Owner command {strike.FinishNotification}/{strike.FinishArgument} remains pending after the transition visual.";
        var duration = definition.Duration(rank);
        if (duration <= 0) duration = particle is null ? float.PositiveInfinity :
            VisualLifetime?.Invoke(particle) ?? particle.EmissionDurationSeconds ?? particle.StrikeSymbol?.Lifetime ?? float.PositiveInfinity;
        // Provisional refresh policy for these authored attached channels. Different casters stay independent.
        foreach (var id in _effects.Where(pair => !ground && pair.Value.Caster == caster.BaseState.ObjectReference &&
                    pair.Value.CasterInstance == caster.InstanceVersion && pair.Value.Owner == owner &&
                    pair.Value.Definition.Code == definition.Code && pair.Value.Definition.Kind == definition.Kind &&
                    pair.Value.Channel == channel).Select(pair => pair.Key).ToArray()) _effects.Remove(id);
        var idNew = --_generation;
        _effects[idNew] = new(idNew, definition, caster.BaseState.ObjectReference, caster.InstanceVersion,
            owner, wearer?.InstanceVersion ?? 0, channel, rank, duration, pending)
        { Age = MathF.Max(0, age), VisualAvailable = visualAvailable, GroundTarget = ground ? groundTarget : null };
        _log($"Status FX created: caster {caster.BaseState.ObjectReference}; owner {owner}; art {definition.Code}; resource {definition.Resource}; native subtype {definition.FxSubtype}; bone {definition.Bone}; duration {duration}; rank {rank}. {pending}");
    }

    private static GoldObjectPosition? GroundTarget(GoldCombatEvent action) => action switch
    {
        GoldCreatureCastEvent cast => new(cast.TargetX, cast.TargetY, cast.TargetSurface, cast.TargetSector),
        _ => null
    };

    private int Rank(GoldReplicatedObject actor, uint code)
    {
        var values = actor.Values;
        if (values is not null)
            for (var i = 0; i < values.Count22; i++)
            {
                var record = MemoryMarshal.Read<SacredCombatArtRuntimeLayout>(values.GetRecord22(i));
                if (record.Code == code) return record.BaseLevel + record.BonusLevel;
            }
        if (actor.BaseState.ObjectReference == _world.LocalPlayerSlot && _world.LocalCharacterValues is { } saved)
            for (var i = 0; i < saved.Count22; i++)
            {
                var record = MemoryMarshal.Read<SacredCombatArtRuntimeLayout>(saved.GetRecord22(i));
                if (record.Code == code) return record.BaseLevel + record.BonusLevel;
            }
        return 1; // Native spell-release code initializes the level to one before lookup.
    }
    private bool Alive(uint reference, long instance) => _world.Objects.TryGetValue(reference, out var obj) &&
        obj.InstanceVersion == instance && (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 &&
        obj.CurrentHitPoints is not <= 0;
    public void Clear()
    {
        _effects.Clear();
        _actions.Clear();
        _world.Consumers.ResetToPresent(_cursor);
    }
    private sealed class PendingAction(GoldCombatNotification notification, SacredCombatArtKind kind, uint code, uint target, long? targetInstance)
    {
        public GoldCombatNotification Notification { get; } = notification;
        public SacredCombatArtKind Kind { get; } = kind;
        public uint Code { get; } = code;
        public uint Target { get; } = target;
        public long? TargetInstance { get; } = targetInstance;
        public float Age { get; set; }
        public float? AnimationSeconds { get; set; }
        public HashSet<int> Released { get; } = [];
    }
}
