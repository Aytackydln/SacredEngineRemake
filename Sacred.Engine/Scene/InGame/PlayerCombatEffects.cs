using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.CombatArts;
using Sacred.GameplayHost.World;
using Sacred.Granny.Animation;
using Sacred.Particles;
using Sacred.World.Particles;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Decoded startup and released visuals, attached to the player or fixed terrain targets.</summary>
internal sealed class PlayerCombatEffects(WorldParticleSystem particles)
{
    private readonly List<Effect> _effects = [];
    private long _generation;
    public int Count => _effects.Count;

    public int Start(ushort code, SacredCombatArtKind kind, uint type, short castSpeed, bool selfTarget)
    {
        var count = _effects.Count;
        if (kind == SacredCombatArtKind.Spell)
            foreach (var attachment in GameplayCastAttachments.Select(code))
                Add(791, attachment.Preset, attachment.Bone, castSpeed * .01f);
        else if (code == 1018 && type is 1 or 0x8D) Add(844, code, "Bip01 R Finger3", 1);
        else if (code is 1012 or 1015 && type == 2) Add(803, code, "Bip01 R Finger3", 1);
        else if (GameplayDaemonTargetedArt.Handles(type, code) && selfTarget)
            foreach (var attachment in GameplayDaemonTargetedArt.Attachments(code))
                Add(791, attachment.Preset, attachment.Bone, 1);
        return _effects.Count - count;
    }

    private void Add(uint type, int preset, string bone, float clock)
    {
        if (clock <= 0 || !particles.Catalogue.TryGetEventDefinition(type, preset, out var definition) ||
            definition.Status != SacredParticleDefinitionStatus.Decoded) return;
        var drain = definition.ParameterSets.Select(set => MathF.Min(
            set.Motion.FadeChangeRate < 0 ? 255 / -set.Motion.FadeChangeRate : float.PositiveInfinity,
            set.Motion.SizeChangeRate < 0 ? (set.Emission.Size + MathF.Abs(set.Emission.SizeRandomWidth)) / -set.Motion.SizeChangeRate : float.PositiveInfinity))
            .DefaultIfEmpty(0).Max();
        if (definition.ParameterSets.All(set => set.Emission.EmissionInterval <= 0 && set.Emission.BurstCount == 0)) drain = 0;
        var duration = (definition.EmissionDurationSeconds ?? 0) / clock + drain + .2f;
        if (!float.IsFinite(duration)) return;
        _effects.Add(new(++_generation, type, preset, bone, clock, duration));
        EngineLog.WriteLine($"Player combat FX: resource {type}; preset {preset}; bone {bone}; clock {clock:0.###}.");
    }

    public void Release(ushort code, SacredCombatArtKind kind, Vector2 target, float groundHeight, uint? radius, float initialAge)
    {
        var refreshed = new HashSet<uint>();
        foreach (var visual in SacredCombatVisualCatalogue.Find(kind, code))
        {
            if (!particles.Catalogue.TryGetEventDefinition(visual.Resource, 0, out var definition) ||
                !WorldAttachedParticleEffects.SupportsSurface(definition)) continue;
            var duration = visual.Duration(1);
            if (duration <= 0) duration = particles.FxAnimations.Lifetime(definition) ?? float.PositiveInfinity;
            // Buff previews use the player as the native destination. Ground casts retain their click point.
            // Recasting a wearer-bound effect refreshes it; independent ground casts may coexist.
            if (visual.Bone.Length > 0 && refreshed.Add(visual.Resource))
                _effects.RemoveAll(effect => effect.Released && effect.Type == visual.Resource);
            _effects.Add(new(++_generation, visual.Resource, 0, visual.Bone, 1, duration)
            { Released = true, Point = visual.Bone.Length == 0 ? target : null, Radius = radius, GroundHeight = groundHeight,
                Age = initialAge, Direction = visual.Direction });
            EngineLog.WriteLine($"Player combat release: art {code}; FX {visual.Resource}; CA level 1; duration {duration:0.###}s; placement {(visual.Bone.Length == 0 ? $"ground {target}" : visual.Bone)}.");
        }
    }

    public void Update(PlayerCombatContext context, float seconds)
    {
        foreach (var effect in _effects)
        {
            effect.Age += MathF.Max(0, seconds);
            if (!float.IsFinite(effect.Duration) && particles.Catalogue.TryGetEventDefinition(effect.Type, effect.Preset, out var definition) &&
                particles.FxAnimations.Lifetime(definition) is { } duration) effect.Duration = duration;
        }
        _effects.RemoveAll(effect => effect.Age >= effect.Duration);
        var states = new List<WorldAttachedParticleEffect>(_effects.Count);
        foreach (var effect in _effects.Where(effect => effect.Point.HasValue || effect.Bone == "__Root"))
        {
            var root = GameplayProjectileCoordinates.FromScene(context.Model.Transform.Translation);
            var position = effect.Point ?? GameplayProjectileCoordinates.ToTiles(root);
            states.Add(State(effect, position, effect.Point.HasValue ? effect.GroundHeight : root.Z));
        }
        if (_effects.Count > 0 && context.Model.Geometry.Animation is { } mesh && mesh.Playback.LastRequest is { } request)
        {
            // GPU skinning need not materialize a CPU mesh. Only the bone palette is needed.
            var evaluator = new GrnPoseEvaluator(mesh.Pose.Data, request.Animation);
            var pose = request.Blend is { } blend ? evaluator.EvaluateBlended(request.TimeSeconds, request.TimeMode, blend)
                : request.TimeMode == GrnPoseTimeMode.Clamp ? evaluator.EvaluateClamped(request.TimeSeconds) : evaluator.Evaluate(request.TimeSeconds);
            foreach (var effect in _effects.Where(effect => !effect.Point.HasValue && effect.Bone != "__Root"))
            {
                Vector3 local;
                if (pose.Data.TryFindBone(effect.Bone, out var bone))
                    local = pose.Data.Projection.Project(pose.BoneWorldTransforms[bone].Translation);
                else
                {
                    var helper = context.Asset.Model.Diagnostics?.Slices.SelectMany(slice => slice.Bones)
                        .FirstOrDefault(candidate => candidate.Name.Equals(effect.Bone, StringComparison.OrdinalIgnoreCase));
                    if (helper?.AnimationBoneName is not { } parent ||
                        !GrnPoseQueries.TryTransformRigidPoint(pose, parent, helper.Position, out local)) continue;
                }
                var point = GameplayProjectileCoordinates.FromScene(Vector3.Transform(local, context.Model.Transform));
                states.Add(State(effect, GameplayProjectileCoordinates.ToTiles(point), point.Z));
            }
        }
        particles.ActorEffects.Synchronize(states);
    }

    private static WorldAttachedParticleEffect State(Effect effect, Vector2 point, float height) =>
        new(effect.Id, effect.Type, effect.Preset, point, height, MathF.Max(0, effect.Age), effect.Clock)
        { DurationSeconds = effect.Released ? effect.Duration : null, ActorBlockRadius = effect.Radius, Direction = effect.Direction };

    public void Clear() => _effects.Clear();
    private sealed class Effect(long id, uint type, int preset, string bone, float clock, float duration)
    {
        public long Id { get; } = id;
        public uint Type { get; } = type;
        public int Preset { get; } = preset;
        public string Bone { get; } = bone;
        public float Clock { get; } = clock;
        public float Duration { get; set; } = duration;
        public Vector3 Direction { get; init; }
        public float Age { get; set; }
        public bool Released { get; init; }
        public Vector2? Point { get; init; }
        public uint? Radius { get; init; }
        public float GroundHeight { get; init; }
    }
}
