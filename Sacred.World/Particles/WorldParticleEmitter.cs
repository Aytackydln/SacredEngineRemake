using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

internal sealed class WorldParticleEmitter
{
    private const float InitialFade = 255.0f;

    private readonly WorldParticleScriptPlacement _placement;
    private readonly SacredParticleParameterSet[] _parameterSets;
    private float _emissionElapsed;
    private readonly ParticleSimulationBatch[] _batches;
    private readonly float _totalEmissionWeight;
    private int _particleCount;
    private readonly ParticleSpriteReference _sprite;
    private readonly SacredParticleProjection _projection;
    private readonly Random _random;
    private readonly int _capacity;
    private int _nextDrawOrder;
    private Vector2? _origin;
    private float? _heightOffset;

    public WorldParticleEmitter(
        WorldParticleScriptPlacement placement,
        float worldUnitsPerTile,
        SacredParticleProjection projection)
    {
        _placement = placement;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldUnitsPerTile);
        _projection = projection;
        _parameterSets = placement.Definition.ParameterSets
            .Where(p => IsEmittingSet(p) || placement.Definition.InitialParticles.Count > 0 || placement.Definition.ModelBurstCount > 0)
            .ToArray();
        _capacity = placement.Definition.Capacity;
        _batches = _parameterSets.Select(parameters => new ParticleSimulationBatch(parameters, _capacity)).ToArray();
        _totalEmissionWeight = _parameterSets.Sum(parameters => parameters.Emission.EmissionInterval > 0 ? 1 / parameters.Emission.EmissionInterval : 0);
        _sprite = new ParticleSpriteReference(
            placement.Definition.Draw!.TextureName,
            placement.Definition.Draw.AtlasSide,
            placement.Definition.Draw.AtlasSide,
            placement.Definition.Draw.AtlasSide * placement.Definition.Draw.AtlasSide,
            1.0f,
            ParticleShaderKind.ItemParticle);
        foreach (var native in placement.Definition.InitialParticles)
        {
            var batch = _batches.First(b => b.Parameters.Index == native.ParameterSetId);
            batch.Add(new ParticleSimulationState
            {
                Position = native.Position.Value, Velocity = native.Velocity.Value,
                Gravity = native.Mass, Size = native.Size, Fade = native.Energy,
                Rotation = native.Phi, AngularVelocity = native.Moment,
                AtlasCell = native.Frame, DrawOrder = _nextDrawOrder++
            });
            _particleCount++;
        }
        _random = new Random(unchecked(placement.ScriptOffset * 397 ^ (int)placement.Creation.TypeId));

    }

    public bool CanEmit => _parameterSets.Length > 0;
    public ParticleSimulationMode SimulationMode { get; set; } = ParticleSimulationMode.CpuSimd;
    public int ParticleCount => _particleCount;

    public void SetOrigin(Vector2 origin, float heightOffset)
    {
        _origin = origin;
        _heightOffset = heightOffset;
    }

    public void SeedModel(IReadOnlyList<Vector3> vertices)
    {
        var count = Math.Min(vertices.Count, Math.Min(_capacity, _placement.Definition.ModelBurstCount));
        if (count == 0) return;
        var stride = Math.Max(1, vertices.Count / count);
        var parameters = _parameterSets[0];
        var emission = parameters.Emission;
        for (var i = 0; i < count; i++)
        {
            _batches[0].Add(new ParticleSimulationState
            {
                Position = RandomVector(vertices[i * stride], emission.PositionRandomWidth.Value),
                Velocity = RandomVector(emission.Velocity.Value, emission.VelocityRandomWidth.Value),
                Gravity = RandomScalar(emission.Gravity, emission.GravityRandomWidth),
                Size = RandomScalar(emission.Size, emission.SizeRandomWidth),
                Rotation = RandomScalar(emission.Rotation, emission.RotationRandomWidth),
                AngularVelocity = RandomScalar(emission.AngularVelocity, emission.AngularVelocityRandomWidth),
                AtlasCell = emission.VariantSelection <= 1 ? 0 : _random.Next(emission.VariantSelection),
                Fade = InitialFade, DrawOrder = _nextDrawOrder++
            });
            _particleCount++;
        }
    }

    public void EmitBurst()
    {
        foreach (var parameters in _parameterSets)
        {
            if (parameters.Emission.EmissionInterval > 0) continue;
            for (var i = 0u; i < parameters.Emission.BurstCount && _particleCount < _capacity; i++)
                Spawn(parameters);
        }
    }

    public void Update(float deltaSeconds, List<WorldParticle> output, bool emitting = true)
    {
        // Preserve each native updater's birth/integration order.
        if (emitting && _placement.Definition.EmitBeforeMovement) EmitParticles(deltaSeconds);
        UpdateParticles(deltaSeconds);
        if (emitting && !_placement.Definition.EmitBeforeMovement) EmitParticles(deltaSeconds);

        if (emitting && _placement.Definition.Halo is { } halo) AddHalo(halo, output);

        foreach (var batch in _batches)
        for (var index = 0; index < batch.Count; index++)
        {
            var particle = batch[index];
            var parameters = batch.Parameters;
            if (particle.Fade <= 0 || particle.Size <= 0) continue;
            var local = particle.Position;
            var ground = IsometricProjection.IsoToWorld(_projection.Project(new Vector3(local.X, local.Y, 0)));
            var color = WorldParticleAppearance.Color(_placement.Definition.Draw!, parameters, particle.Fade);
            var height = ((_heightOffset ?? _placement.Creation.HeightOffset ?? 0) + local.Z) *
                         _projection.HeightFactor * _projection.VerticalScale;
            var depthAnchor = _origin ?? (_placement.Creation.TilePosition is { } tile
                ? new Vector2(tile.X, tile.Y)
                : new Vector2(_placement.WorldX, _placement.WorldY));
            var origin = _origin ?? new Vector2(_placement.WorldX, _placement.WorldY);
            output.Add(new WorldParticle(
                _placement.ScriptOffset,
                _sprite,
                origin.X + ground.X,
                origin.Y + ground.Y,
                height,
                2 * particle.Size * _projection.HorizontalScale,
                (color >> 24) / InitialFade,
                particle.DrawOrder)
            {
                Color = color,
                // Native pitch has cot(pitch)=2. Screen-space height therefore
                // contributes half as much camera depth as ground displacement.
                PainterDepthKey = WorldPainterDepth.FromWorld(depthAnchor + ground) +
                                  height / IsometricProjection.StepWidth,
                AtlasCell = _placement.Definition.Draw!.UsesRandomAtlasCell ? particle.AtlasCell :
                    Math.Clamp((int)((InitialFade - particle.Fade) * _sprite.FrameCount / 256), 0, _sprite.FrameCount - 1),
                Rotation = particle.Rotation,
                Additive = _placement.Definition.EmissionMode == 2 ? parameters.Index == 0 :
                    (_placement.Definition.Draw.RawFlags & 1) != 0,
                SourceColorOnly = (_placement.Definition.Draw.RawFlags & 0x10) != 0,
                RenderHeight = 2 * particle.Size * _projection.VerticalScale
            });
        }
    }

    private void AddHalo(SacredParticleHaloDefinition halo, List<WorldParticle> output)
    {
        var origin = _origin ?? new Vector2(_placement.WorldX, _placement.WorldY);
        var height = (_heightOffset ?? _placement.Creation.HeightOffset ?? 0) *
                     _projection.HeightFactor * _projection.VerticalScale;
        output.Add(new WorldParticle(_placement.ScriptOffset,
            new ParticleSpriteReference(halo.TextureName, 1, 1, 1, 1, ParticleShaderKind.ItemParticle),
            origin.X, origin.Y, height, 2 * halo.HalfSize * _projection.HorizontalScale,
            (halo.Color >> 24) / InitialFade, -1)
        {
            Color = halo.Color, Additive = true, SourceColorOnly = true,
            RenderHeight = 2 * halo.HalfSize * _projection.VerticalScale,
            PainterDepthKey = WorldPainterDepth.FromWorld(origin) + height / IsometricProjection.StepWidth
        });
    }

    private void UpdateParticles(float deltaSeconds)
    {
        _particleCount = 0;
        foreach (var batch in _batches)
        {
            batch.Update(deltaSeconds, GravityDirection(batch.Parameters), SimulationMode,
                _placement.Definition.GroundCollision, -(_heightOffset ?? _placement.Creation.HeightOffset ?? 0));
            _particleCount += batch.Count;
        }
    }

    private void EmitParticles(float deltaSeconds)
    {
        if (_parameterSets.Length == 0) return;
        var interval = _parameterSets[0].Emission.EmissionInterval;
        if (interval <= 0) return;
        _emissionElapsed += deltaSeconds;
        while (_emissionElapsed >= interval && _particleCount < _capacity)
        {
            _emissionElapsed -= interval;
            Spawn(SelectParameters());
        }
        if (_particleCount == _capacity) _emissionElapsed = Math.Min(_emissionElapsed, interval);
    }

    private SacredParticleParameterSet SelectParameters()
    {
        if (_placement.Definition.EmissionMode == 2)
            return _parameterSets[_random.NextDouble() < 0.8 ? 0 : 1];
        if (_placement.Definition.EmissionMode != 3) return _parameterSets[0];
        var selection = _random.NextDouble() * _totalEmissionWeight;
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
        var particle = new ParticleSimulationState
        {
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
        ParticleCpuSimulation.Advance(ref particle, parameters.Motion, _emissionElapsed,
            GravityDirection(parameters), applyInwardAcceleration: false);
        foreach (var batch in _batches)
        {
            if (!ReferenceEquals(batch.Parameters, parameters)) continue;
            batch.Add(particle);
            _particleCount++;
            break;
        }
    }

    private Vector3 GravityDirection(SacredParticleParameterSet parameters) =>
        _placement.Definition.UsesWind && parameters.Index < 2
            ? new Vector3(_projection.DefaultWind, 0, 0) : parameters.Motion.GravityDirection.Value;

    private float RandomScalar(float center, float width) =>
        center + ((float)_random.NextDouble() * 2 - 1) * width;

    private Vector3 RandomVector(Vector3 center, Vector3 width) => new(
        RandomScalar(center.X, width.X),
        RandomScalar(center.Y, width.Y),
        RandomScalar(center.Z, width.Z));

    private static bool IsEmittingSet(SacredParticleParameterSet parameters) =>
        parameters.Emission.Size > 0.0f &&
        (parameters.Emission.EmissionInterval > 0.0f || parameters.Emission.BurstCount > 0);

}
