using System.Numerics;
using Sacred.Particles;
using Sacred.Particles.Diagnostics;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
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
    private readonly SeededParticleRandom _random;
    private readonly int _capacity;
    private int _nextDrawOrder;
    private bool _burstPending;
    private float _age;
    public bool NativeRetired { get; private set; }
    private bool _nativeEmissionInitialized, _movementReady;
    private float _nativeReleaseTime;
    private float _emissionTime;
    private Vector2? _origin;
    private float? _heightOffset;
    private readonly ParticleEmissionCyclePlayback? _cycle;
    private readonly ParticleLineEmissionPlayback? _line;
    private int _lineBirth;

    public WorldParticleEmitter(
        WorldParticleScriptPlacement placement,
        float worldUnitsPerTile,
        SacredParticleProjection projection)
    {
        _placement = placement;
        _burstPending = placement.Definition.BurstOnFirstUpdate;
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
                AtlasCell = native.Frame, DrawOrder = _nextDrawOrder++, Color = native.Color
            });
            _particleCount++;
        }
        _random = new SeededParticleRandom(unchecked(placement.ScriptOffset * 397 ^ (int)placement.Creation.TypeId));
        if (placement.Definition.EmissionCycle is { } cycle)
            _cycle = new(cycle, unchecked((uint)(placement.ScriptOffset * 397 ^ (int)placement.Creation.TypeId)));
        if (placement.Definition.LineEmission is not null) _line = new();
        if (placement.Definition.OrbitEmission is not null) _point = new();
    }

    public bool CanEmit => _parameterSets.Length > 0;
    internal WorldParticleScriptPlacement Placement => _placement;
    internal WorldParticleEmissionSnapshot Capture()
    {
        foreach (var batch in _batches) batch.RestoreCpu();
        return new(_random.Capture(), _emissionElapsed, _nextDrawOrder,
            _batches.Select(batch => Enumerable.Range(0, batch.Count).Select(i => batch[i]).ToArray()).ToArray())
            { BurstPending = _burstPending, Age = _age, NativeRetired = NativeRetired, NativeEmissionInitialized = _nativeEmissionInitialized,
                MovementReady = _movementReady, NativeReleaseTime = _nativeReleaseTime, OrbitAngle = _orbitAngle,
                ReleasedPoint = _point?.Released,
                CycleCountdown = _cycle?.Countdown ?? 0, CycleRandomState = _cycle?.RandomState ?? 0,
                CycleBurst = _cycle?.Burst ?? false, CurrentLine = _line?.Current, ReleasedLine = _line?.Released };
    }
    internal WorldParticleEmitter(WorldParticleScriptPlacement placement, float units,
        SacredParticleProjection projection, WorldParticleEmissionSnapshot snapshot) : this(placement, units, projection)
    {
        _random.Restore(snapshot.RandomState); _emissionElapsed = snapshot.Elapsed;
        _burstPending = snapshot.BurstPending;
        _age = snapshot.Age;
        NativeRetired = snapshot.NativeRetired;
        _nativeEmissionInitialized = snapshot.NativeEmissionInitialized;
        _movementReady = snapshot.MovementReady;
        _nativeReleaseTime = snapshot.NativeReleaseTime;
        _orbitAngle = snapshot.OrbitAngle;
        if (_point is not null && snapshot.ReleasedPoint is { } releasedPoint) _point.Restore(releasedPoint);
        _nextDrawOrder = snapshot.NextDrawOrder;
        _batches = _parameterSets.Select(p => new ParticleSimulationBatch(p, _capacity)).ToArray();
        _particleCount = 0;
        if (_line is not null && snapshot.CurrentLine is { } current && snapshot.ReleasedLine is { } released)
            _line.Restore(current, released);
        if (_cycle is not null)
        {
            _cycle.Restore(snapshot.CycleCountdown, snapshot.CycleRandomState, snapshot.CycleBurst);
            ApplyCycleParameters();
        }
        for (var set = 0; set < _batches.Length; set++)
        foreach (var particle in snapshot.Particles[set]) { _batches[set].Add(particle); _particleCount++; }
    }
    private ParticleSimulationMode _simulationMode = ParticleSimulationMode.CpuSimd;
    public ParticleSimulationMode SimulationMode
    {
        get => _simulationMode;
        set
        {
            // These native draws share one particle vector or use an independent flare stack.
            // The standard GPU emitter ABI has a single draw/halo slot.
            if (value is ParticleSimulationMode.Gpu or ParticleSimulationMode.GpuOnly &&
                (_placement.Definition.AdditionalDraws.Count > 0 || _placement.Definition.AdditionalHalos.Count > 0 ||
                 _placement.Definition.RetireAfterDuration)) value = ParticleSimulationMode.CpuSimd;
            if (value is not (ParticleSimulationMode.Gpu or ParticleSimulationMode.GpuOnly))
                foreach (var batch in _batches) batch.RestoreCpu();
            _simulationMode = value;
        }
    }
    public IParticleGpuBackend? GpuBackend
    {
        set { foreach (var batch in _batches) batch.GpuBackend = value; }
    }
    public void CollectGpuBatches(List<WorldGpuParticleBatch> output)
    {
        var origin = _origin ?? new Vector2(_placement.WorldX, _placement.WorldY);
        var depthAnchor = _origin ?? (_placement.Creation.TilePosition is { } tile
            ? new Vector2(tile.X, tile.Y) : origin);
        foreach (var batch in _batches)
            if (batch.Gpu is { } gpu)
                output.Add(new(gpu, _placement.Definition, _sprite, _projection, origin, depthAnchor,
                    _heightOffset ?? _placement.Creation.HeightOffset ?? 0));
    }
    public int ParticleCount => _particleCount;
    public bool EmissionFinished => OrbitFinished || _placement.Definition.EmissionDurationSeconds is { } duration &&
        (_placement.Definition.EmissionClock == SacredParticleEmissionClock.CrossingUpdate ? _age > duration : _age >= duration);

    public void SetOrigin(Vector2 origin, float heightOffset)
    {
        _origin = origin;
        _heightOffset = heightOffset;
    }

    public void SeedModel(IReadOnlyList<Vector3> vertices)
    {
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.Births);
        var count = Math.Min(vertices.Count, Math.Min(_capacity, _placement.Definition.ModelBurstCount));
        if (count == 0) return;
        ParticlePerformance.RecordBirths(count);
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
                Fade = InitialFade, DrawOrder = _nextDrawOrder++, Color = emission.Color
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
        if (SimulationMode == ParticleSimulationMode.GpuOnly &&
            _batches.Any(batch => batch.GpuBackend is not { IsAvailable: true })) return;
        var previousAge = _age;
        _age += deltaSeconds;
        var definition = _placement.Definition;
        if (definition.RetireAfterDuration && definition.EmissionDurationSeconds is { } lifetime &&
            (double)previousAge + deltaSeconds > lifetime) NativeRetired = true;
        PrepareOrbit();
        emitting &= !OrbitFinished;
        var showHalo = emitting;
        _emissionTime = definition.EmissionClock == SacredParticleEmissionClock.PreviousTimeExclusive ? previousAge : _age;
        emitting &= definition.EmissionDurationSeconds is not { } duration || definition.EmissionClock switch
        {
            SacredParticleEmissionClock.PreviousTimeExclusive => previousAge < duration,
            SacredParticleEmissionClock.CurrentTimeExclusive => _age < duration,
            SacredParticleEmissionClock.CurrentTimeInclusive => _age <= duration,
            _ => previousAge <= duration
        };
        var skipMovement = _burstPending && definition.SkipFirstMovement;
        // Preserve each native updater's birth/integration order.
        if (_burstPending && _placement.Definition.EmitBeforeMovement) EmitPendingBurst();
        if (emitting && _placement.Definition.EmitBeforeMovement) EmitParticles(deltaSeconds);
        if (!skipMovement) UpdateParticles(deltaSeconds);
        if (_cycle is not null)
        {
            var wasBurst = _cycle.Burst; _cycle.Advance(deltaSeconds);
            if (wasBurst != _cycle.Burst) ApplyCycleParameters();
        }
        if (_burstPending) EmitPendingBurst();
        if (emitting && !_placement.Definition.EmitBeforeMovement) EmitParticles(deltaSeconds);
        AdvanceOrbit(deltaSeconds);

        if (!NativeRetired) AppendParticles(definition, showHalo, output);
    }

    public void SetLine(ParticleEmissionLine line) => _line?.Set(line);

    private void EmitPendingBurst()
    {
        EmitBurst();
        _burstPending = false;
    }

    private void ApplyCycleParameters()
    {
        _parameterSets[0] = _placement.Definition.EmissionCycle!.Apply(_parameterSets[0], _cycle!.Burst);
        _batches[0].ApplyParameters(_parameterSets[0]);
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
        _movementReady = true;
    }

    private void EmitParticles(float deltaSeconds)
    {
        if (_parameterSets.Length == 0) return;
        if (_placement.Definition.SingleBirthInitialization && !_nativeEmissionInitialized)
        {
            if (!_movementReady || _particleCount == _capacity) return;
            _nativeEmissionInitialized = _emissionTime != 0;
            _nativeReleaseTime = _emissionTime;
            _lineBirth = 0;
            Spawn(SelectParameters());
            _line?.Initialize(_parameterSets[0].Emission.PositionOffset.Value);
            _point?.Initialize(_parameterSets[0].Emission.PositionOffset.Value);
            return;
        }
        var interval = _parameterSets[0].Emission.EmissionInterval;
        if (interval <= 0) return;
        if (_placement.Definition.SingleBirthInitialization)
        {
            // Native stores elapsed before division, then rounds the release clock once
            // after all births. Repeated float subtraction can produce an extra birth.
            var elapsed = _emissionTime - _nativeReleaseTime;
            var count = (int)Math.Min((double)elapsed / interval, _capacity - _particleCount);
            if (count > 0) _line?.Begin(elapsed, interval, _parameterSets[0].Emission.PositionOffset.Value);
            if (count > 0) _point?.Begin(elapsed, interval, _parameterSets[0].Emission.PositionOffset.Value);
            for (var birth = 1; birth <= count; birth++)
            {
                _lineBirth = birth;
                _emissionElapsed = (float)((double)_emissionTime - _nativeReleaseTime - birth * (double)interval);
                Spawn(SelectParameters());
            }
            _line?.Commit(count);
            if (count > 0) _point?.Commit(count);
            _nativeReleaseTime = (float)(_nativeReleaseTime + count * (double)interval);
            _emissionElapsed = _emissionTime - _nativeReleaseTime;
            return;
        }
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
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.Births);
        ParticlePerformance.RecordBirths(1);
        var position = parameters.Emission.PositionOffset.Value;
        if (_point is not null && _lineBirth != 0) position = _point.Birth(_lineBirth);
        if (_line is not null)
            position = _lineBirth == 0 ? _line.FirstBirth(position, _random.NextDouble()) : _line.Birth(_lineBirth, _random.NextDouble());
        ParticleSimulationState particle;
        using (ParticlePerformance.Measure(ParticleCpuStage.SpawnPreAdvance))
            particle = ParticleBirthStateFactory.Create(parameters, _placement.Definition, _random,
                position, _emissionElapsed, GravityDirection(parameters), _nextDrawOrder++);
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
