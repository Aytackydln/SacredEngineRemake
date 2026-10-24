using System.Numerics;
using Sacred.Core.GameBin.Scripts;
using Sacred.Granny.Animation;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Native cast01 helper animation and its draining three-variant particle vector.</summary>
internal sealed class WorldVampireTransformationPlayback
{
    private readonly SacredParticleDefinition _definition;
    private readonly WorldFxAnimations _animations;
    private WorldParticleEmitter? _emitter;
    private GrnAnimationClip? _clip;
    private float _age;
    private Vector3 _tip;
    private WorldAttachedParticleEffect? _origin;
    private readonly List<WorldParticle> _discarded = [];
    public WorldVampireTransformationPlayback(SacredParticleDefinition definition, WorldFxAnimations animations)
        => (_definition, _animations) = (definition, animations);
    public bool Active => _clip is null || _age <= _clip.DurationSeconds || _emitter?.ParticleCount > 0;

    public void Update(WorldAttachedParticleEffect state, SacredParticleProjection projection, List<WorldParticle> output)
    {
        var fx = _definition.VampireTransformation!;
        _origin ??= state;
        state = state with { Position = _origin.Position, Height = _origin.Height, Direction = _origin.Direction };
        if (_clip is null)
        {
            _clip = _animations.Find(fx.Animation);
            if (_clip is null) return;
            _emitter = new(new WorldParticleScriptPlacement(unchecked((int)state.Generation),
                new SacredScriptCreateObject(null, _definition.TypeId, null, null, null), _definition, 0, 0), 1, projection);
            Console.WriteLine($"FX animation ready: {_clip.Name}; source {fx.SourceBone}; duration {_clip.DurationSeconds:0.###}s.");
        }
        _emitter!.SetOrigin(state.Position, state.Height);
        var remaining = MathF.Max(0, state.Age - _age);
        while (remaining > 1f / 60) { Advance(1f / 60); remaining -= 1f / 60; }
        if (remaining > 0) Advance(remaining);
        _emitter.AppendCurrentParticles(output);
        if (_age > 0 && _age <= _clip.DurationSeconds)
        {
            var writer = new WorldFxSpriteWriter(state, projection, output);
            writer.Add(fx.GlowTexture, fx.GlowHalfSize, fx.GlowHalfSize, fx.GlowColor, _tip, false, sourceColorOnly: true);
            writer.Add(fx.SparkTexture, fx.SparkHalfSize, fx.SparkHalfSize, fx.SparkColor, _tip, false, sourceColorOnly: true);
        }

        void Advance(float seconds)
        {
            // Native samples at the previous age, rotates XY by cEventFX+2C, then integrates and emits.
            var position = GrnAnimationBoneSampler.SamplePosition(_clip, fx.SourceBone, _age);
            // cGranny reflects model and animation Y when loading the original resources.
            position.Y = -position.Y;
            var sin = MathF.Sin(state.Direction.X); var cos = MathF.Cos(state.Direction.X);
            _tip = new(position.X * cos - position.Y * sin, position.Y * cos + position.X * sin, position.Z);
            _emitter.SetAnimatedSource(_tip);
            _discarded.Clear();
            _emitter.Update(seconds, _discarded, _age <= _clip.DurationSeconds);
            _age += seconds;
        }
    }
}
