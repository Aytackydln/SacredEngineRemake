using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

internal sealed class WorldParticleEmitter
{
    private const float InitialFade = 255.0f;
    private const int SmokeCapacity = 200;
    private const int DwarfMagicCapacity = 100;

    private readonly WorldParticleScriptPlacement _placement;
    private readonly SacredParticleParameterSet[] _parameterSets;
    private float _emissionElapsed;
    private readonly List<ParticleState> _particles;
    private readonly ParticleSpriteReference _sprite;
    private readonly SacredParticleProjection _projection;
    private readonly Random _random;
    private readonly int _capacity;
    private int _nextDrawOrder;

    public WorldParticleEmitter(
        WorldParticleScriptPlacement placement,
        float worldUnitsPerTile,
        SacredParticleProjection projection)
    {
        _placement = placement;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldUnitsPerTile);
        _projection = projection;
        _parameterSets = placement.Definition.ParameterSets
            .Where(IsEmittingSet)
            .ToArray();
        _capacity = placement.Definition.NativeClass switch
        {
            "cParticleSystem_dwarfmagic" => DwarfMagicCapacity,
            _ => SmokeCapacity
        };
        _particles = new List<ParticleState>(_capacity);
        _sprite = new ParticleSpriteReference(
            placement.Definition.Draw!.TextureName,
            placement.Definition.Draw.AtlasSide,
            placement.Definition.Draw.AtlasSide,
            placement.Definition.Draw.AtlasSide * placement.Definition.Draw.AtlasSide,
            1.0f,
            ParticleShaderKind.ItemParticle);
        _random = new Random(unchecked(placement.ScriptOffset * 397 ^ (int)placement.Creation.TypeId));

    }

    public bool CanEmit => _parameterSets.Length > 0;

    public void Update(float deltaSeconds, List<WorldParticle> output)
    {
        // Dwarf magic generates before integrating; smoke integrates before generating.
        if (!_placement.Definition.UsesWind) EmitParticles(deltaSeconds);
        UpdateParticles(deltaSeconds);
        if (_placement.Definition.UsesWind) EmitParticles(deltaSeconds);

        foreach (var particle in _particles)
        {
            if (particle.Fade <= 0 || particle.Size <= 0) continue;
            var local = particle.Position;
            var ground = IsometricProjection.IsoToWorld(_projection.Project(new Vector3(local.X, local.Y, 0)));
            var color = WorldParticleAppearance.Color(_placement.Definition.Draw!, particle.Parameters, particle.Fade);
            output.Add(new WorldParticle(
                _placement.ScriptOffset,
                _sprite,
                _placement.WorldX + ground.X,
                _placement.WorldY + ground.Y,
                ((_placement.Creation.HeightOffset ?? 0) + local.Z) * _projection.HeightFactor * _projection.VerticalScale,
                2 * particle.Size * _projection.HorizontalScale,
                (color >> 24) / InitialFade,
                particle.DrawOrder)
            {
                Color = color,
                AtlasCell = _placement.Definition.Draw!.UsesRandomAtlasCell ? particle.AtlasCell :
                    Math.Clamp((int)((InitialFade - particle.Fade) * _sprite.FrameCount / 256), 0, _sprite.FrameCount - 1),
                Rotation = particle.Rotation,
                Additive = _placement.Definition.EmissionMode == 2 ? particle.Parameters.Index == 0 :
                    (_placement.Definition.Draw.RawFlags & 1) != 0,
                SourceColorOnly = (_placement.Definition.Draw.RawFlags & 0x10) != 0,
                RenderHeight = 2 * particle.Size * _projection.VerticalScale
            });
        }
    }

    private void UpdateParticles(float deltaSeconds)
    {
        for (var index = _particles.Count - 1; index >= 0; index--)
        {
            var particle = _particles[index];
            Advance(ref particle, deltaSeconds);
            if (particle.Fade <= 0.0f || particle.Size <= 0.0f)
            {
                _particles.RemoveAt(index);
                continue;
            }

            _particles[index] = particle;
        }
    }

    private void EmitParticles(float deltaSeconds)
    {
        if (_parameterSets.Length == 0) return;
        var interval = _parameterSets[0].Emission.EmissionInterval;
        if (interval <= 0) return;
        _emissionElapsed += deltaSeconds;
        while (_emissionElapsed >= interval && _particles.Count < _capacity)
        {
            _emissionElapsed -= interval;
            Spawn(SelectParameters());
        }
        if (_particles.Count == _capacity) _emissionElapsed = Math.Min(_emissionElapsed, interval);
    }

    private SacredParticleParameterSet SelectParameters()
    {
        if (_placement.Definition.EmissionMode == 2)
            return _parameterSets[_random.NextDouble() < 0.8 ? 0 : 1];
        if (_placement.Definition.EmissionMode != 3) return _parameterSets[0];
        var total = _parameterSets.Sum(p => 1 / p.Emission.EmissionInterval);
        var selection = _random.NextDouble() * total;
        foreach (var set in _parameterSets)
        {
            selection -= 1 / set.Emission.EmissionInterval;
            if (selection < 0) return set;
        }
        return _parameterSets[^1];
    }

    private void Spawn(SacredParticleParameterSet parameters)
    {
        var emission = parameters.Emission;
        var particle = new ParticleState
        {
            Parameters = parameters,
            Position = RandomVector(emission.PositionOffset.Value, emission.PositionRandomWidth.Value),
            Velocity = RandomVector(emission.Velocity.Value, emission.VelocityRandomWidth.Value),
            Gravity = RandomScalar(emission.Gravity, emission.GravityRandomWidth),
            Size = RandomScalar(emission.Size, emission.SizeRandomWidth),
            Rotation = RandomScalar(emission.Rotation, emission.RotationRandomWidth),
            AngularVelocity = RandomScalar(emission.AngularVelocity, emission.AngularVelocityRandomWidth),
            AtlasCell = emission.VariantSelection switch
            {
                0 => 0,
                255 => parameters.Index,
                _ => _random.Next(emission.VariantSelection)
            },
            Fade = InitialFade,
            DrawOrder = _nextDrawOrder++
        };
        // Timed native births are advanced by the unconsumed part of this interval.
        Advance(ref particle, _emissionElapsed, applyInwardAcceleration: false);
        _particles.Add(particle);
    }

    private void Advance(ref ParticleState particle, float deltaSeconds, bool applyInwardAcceleration = true)
    {
        var motion = particle.Parameters.Motion;
        particle.Position += particle.Velocity * deltaSeconds;
        particle.Velocity.Z -= particle.Gravity * deltaSeconds;
        var direction = _placement.Definition.UsesWind && particle.Parameters.Index < 2
            ? new Vector3(_projection.DefaultWind, 0, 0) : motion.GravityDirection.Value;
        particle.Velocity += direction * (particle.Gravity * deltaSeconds);
        if (applyInwardAcceleration && motion.InwardAcceleration != 0 && particle.Position.LengthSquared() > 0.000001f)
            particle.Velocity -= Vector3.Normalize(particle.Position) * (motion.InwardAcceleration * deltaSeconds);
        particle.Fade += motion.FadeChangeRate * deltaSeconds;
        particle.Size += motion.SizeChangeRate * deltaSeconds;
        particle.Rotation += (particle.AngularVelocity + motion.AdditionalAngularVelocity) * deltaSeconds;
        particle.Gravity += motion.GravityChangeRate * deltaSeconds;
    }

    private float RandomScalar(float center, float width) =>
        center + ((float)_random.NextDouble() * 2 - 1) * width;

    private Vector3 RandomVector(Vector3 center, Vector3 width) => new(
        RandomScalar(center.X, width.X),
        RandomScalar(center.Y, width.Y),
        RandomScalar(center.Z, width.Z));

    private static bool IsEmittingSet(SacredParticleParameterSet parameters) =>
        parameters.Emission.Size > 0.0f &&
        (parameters.Emission.EmissionInterval > 0.0f || parameters.Emission.BurstCount > 0);

    private struct ParticleState
    {
        public required SacredParticleParameterSet Parameters;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Gravity;
        public float Size;
        public float Fade;
        public float Rotation;
        public float AngularVelocity;
        public int AtlasCell;
        public int DrawOrder;
    }
}
