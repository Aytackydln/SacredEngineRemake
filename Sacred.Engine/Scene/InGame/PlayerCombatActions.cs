using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.CombatArts;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Animation;
using Sacred.Engine.Assets;
using Sacred.GameplayHost.World;
using Sacred.Granny.Animation;
using Sacred.Particles;
using Sacred.World;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Local visual testing: loads original motions, plays finite actions, and starts decoded FX.</summary>
internal sealed class PlayerCombatActions(AssetManager assets, WorldParticleSystem particles, WorldElevationSampler elevation)
{
    private readonly Dictionary<(string Model, uint Motion), Task<GrnAnimationClip?>> _motions = [];
    private readonly PlayerCombatEffects _effects = new(particles);
    private PlayerCombatContext? _context;
    private Pending? _pending;
    private CharacterAnimationState? _playing;
    private CharacterAnimationState? _attack;
    private float _remaining;
    private Pending? _release;
    private float _releaseIn;
    private ushort _playingCode;
    public string Status { get; private set; } = "Ready: right-click casts; Ctrl+left-click attacks.";
    public bool Active => _pending is not null || _playing is not null || _attack?.CurrentState == CharacterAnimationStateId.Attack;
    public string Diagnostics => $"{Status}; active {Active}; local FX {_effects.Count}; rendered attachments {particles.ActorEffects.ActiveCount}; " +
        $"clip {_context?.Animation.AnimatedMesh?.Playback.LastRequest?.Animation.Name ?? "none"}";

    public bool Attack(PlayerCombatContext? context)
    {
        if (context is not { } current) return Report("Player attack waits for character animation loading.");
        Bind(current);
        StopMotion();
        _attack = current.Animation;
        _attack.PlayAttack();
        Status = "Playing ordinary attack animation (local preview).";
        EngineLog.WriteLine("Debug input: player attack action; local animation.");
        return true;
    }

    public bool Cast(PlayerCombatContext? context, ushort? code, bool selfTarget, Vector2 target)
    {
        if (context is not { } current) return Report("Player cast waits for character animation loading.");
        Bind(current);
        if (code is not { } selected || !current.Actor.CombatArts.Entries.TryGetValue(selected, out var entry) || entry.TotalLevel == 0)
            return Report("Select a learned combat art in a combat quickslot first.");
        var plan = PlayerCombatArtRules.Select(assets, current, selected, entry.Kind, selfTarget, out var castSpeed);
        if (plan is not { } motion)
            return Report($"Combat art {selected}: no supported starter for this form, weapon stance or target. Targeted arts require an eligible creature.");
        StopMotion();
        _pending = new(selected, entry.Kind, motion, castSpeed, selfTarget, target, Load(current.Asset.ModelName, motion.Motion), current);
        Status = $"Loading combat art {selected}, motion {motion.Motion}.";
        EngineLog.WriteLine($"Debug input: player cast {selected}; rank {entry.TotalLevel}; motion {motion.Motion}; target {(selfTarget ? "self" : "world")}; local visual preview.");
        return true;
    }

    public void Update(PlayerCombatContext? context, float seconds)
    {
        if (context is not { } current) { Reset(); particles.ActorEffects.Synchronize([]); return; }
        Bind(current);
        if (_release is { } release && (_releaseIn -= MathF.Max(0, seconds)) <= 0)
        {
            var height = TerrainElevationProjection.ScreenHeight(elevation.SampleHeightOrZero(release.Target)) /
                (particles.Catalogue.Projection.HeightFactor * particles.Catalogue.Projection.VerticalScale);
            _effects.Release(release.Code, release.Kind, release.Target, height,
                assets.GetItem(current.Asset.ItemId)?.ModelDesc.BlockRadius, -_releaseIn - MathF.Max(0, seconds));
            Status = $"CA {release.Code}: released level-1 visual effects.";
            _release = null;
        }
        if (_playing is not null && (_remaining -= MathF.Max(0, seconds)) <= 0)
        { _playing.StopNpcAnimation(); _playing = null; Status = $"CA {_playingCode}: animation finished."; }
        if (_attack is not null && _attack.CurrentState != CharacterAnimationStateId.Attack)
        { _attack = null; Status = "Attack animation finished."; }
        if (_pending is { } pending && pending.Load.IsCompleted)
        {
            var clip = pending.Load.GetAwaiter().GetResult();
            if (clip is null && pending.Plan.FallbackMotion is { } fallback && fallback != pending.Plan.Motion)
            {
                _pending = pending with { Plan = pending.Plan with { Motion = fallback, FallbackMotion = null }, Load = Load(current.Asset.ModelName, fallback) };
            }
            else
            {
                _pending = null;
                if (clip is null) Report($"Combat art {pending.Code}: motion {pending.Plan.Motion} is absent from {current.Asset.ModelName}.");
                else Start(pending, clip);
            }
        }
        _effects.Update(current, seconds);
    }

    private void Start(Pending pending, GrnAnimationClip clip)
    {
        var rate = pending.Plan.Speed;
        var speed = rate * clip.PlaybackSpeedMultiplier * (assets.MovementCalculator?.GlobalAnimationScale ?? 1);
        if (!float.IsFinite(speed) || speed <= 0 || clip.DurationSeconds <= 0)
        { Report($"Combat art {pending.Code}: invalid motion duration or playback speed."); return; }
        var timing = new SacredNpcAnimationTiming(speed, MathF.Min(.2f, pending.Plan.BlendNumerator / rate), 1);
        pending.Context.Animation.PlayNpcAnimation(clip, timing);
        _playing = pending.Context.Animation;
        _playingCode = pending.Code;
        _remaining = clip.DurationSeconds / speed;
        var count = _effects.Start(pending.Code, pending.Kind, PlayerCombatArtRules.CreatureType(assets, pending.Context), pending.CastSpeed, pending.SelfTarget);
        var visual = SacredCombatVisualCatalogue.Find(pending.Kind, pending.Code).FirstOrDefault();
        if (visual is not null)
        { _release = pending; _releaseIn = _remaining * visual.ReleaseProgress; }
        Status = $"CA {pending.Code}: motion {pending.Plan.Motion}; {count} startup FX; release {(_release is null ? "unmapped" : $"in {_releaseIn:0.###}s")}. Local visuals only.";
        EngineLog.WriteLine($"Player combat animation: art {pending.Code}; motion {pending.Plan.Motion}; clip {clip.Name}; speed {speed:0.###}; duration {_remaining:0.###}s; startup FX {count}.");
    }

    private void Bind(PlayerCombatContext context)
    {
        if (_context is { } previous && ReferenceEquals(previous.Actor, context.Actor) &&
            ReferenceEquals(previous.Animation, context.Animation)) return;
        Reset();
        _context = context;
    }

    public void Reset()
    {
        StopMotion();
        _context = null;
        _effects.Clear();
        Status = "Ready: right-click casts; Ctrl+left-click attacks.";
    }

    public void StopMotion()
    {
        _pending = null;
        _release = null;
        _playing?.StopNpcAnimation();
        _attack?.StopAction();
        _playing = null;
        _attack = null;
    }

    private Task<GrnAnimationClip?> Load(string model, uint motion)
    {
        if (!_motions.TryGetValue((model, motion), out var load))
            _motions[(model, motion)] = load = LoadCore(model, motion);
        return load;
    }

    private async Task<GrnAnimationClip?> LoadCore(string model, uint motion)
    {
        try { return await assets.LoadNetworkCombatMotionAsync(model, motion, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) { EngineLog.WriteLine($"Player combat motion {motion} unavailable: {error.Message}"); return null; }
    }

    private bool Report(string message)
    { Status = message; EngineLog.WriteLine(message); return false; }

    private sealed record Pending(ushort Code, SacredCombatArtKind Kind, GameplayCombatArtAnimation Plan,
        short CastSpeed, bool SelfTarget, Vector2 Target, Task<GrnAnimationClip?> Load, PlayerCombatContext Context);
}
