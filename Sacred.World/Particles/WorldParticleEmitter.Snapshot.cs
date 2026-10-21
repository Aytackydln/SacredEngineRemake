using Sacred.Particles;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
{
    internal WorldParticleEmissionSnapshot Capture()
    {
        foreach (var batch in _batches) batch.RestoreCpu();
        return new(_random.Capture(), _emissionElapsed, _nextDrawOrder,
            _batches.Select(batch => Enumerable.Range(0, batch.Count).Select(i => batch[i]).ToArray()).ToArray())
            { BurstPending = _burstPending, Age = _age, ClockScale = ClockScale, NativeRetired = NativeRetired, NativeEmissionInitialized = _nativeEmissionInitialized,
                MovementReady = _movementReady, NativeReleaseTime = _nativeReleaseTime, OrbitAngle = _orbitAngle,
                ReleasedPoint = _point?.Released,
                CycleCountdown = _cycle?.Countdown ?? 0, CycleRandomState = _cycle?.RandomState ?? 0,
                CycleBurst = _cycle?.Burst ?? false, BirthSizeFactor = _birthSizeFactor, Aura = _aura.Capture(),
                CurrentLine = _line?.Current, ReleasedLine = _line?.Released, Emperor = _emperor?.CaptureState(),
                BloodFountain = _bloodFountain?.CaptureState(), MineSign = _mineSign?.CaptureState() };
    }
    internal WorldParticleEmitter(WorldParticleScriptPlacement placement, float units,
        SacredParticleProjection projection, WorldParticleEmissionSnapshot snapshot) : this(placement, units, projection)
    {
        _random.Restore(snapshot.RandomState); _emissionElapsed = snapshot.Elapsed;
        _burstPending = snapshot.BurstPending;
        _age = snapshot.Age;
        ClockScale = snapshot.ClockScale;
        if (_mineSign is not null && snapshot.MineSign is { } sign)
        {
            _mineSign.RestoreState(sign);
            _parameterSets[0] = _parameterSets[0] with { Emission = sign.State.Emission };
        }
        if (_bloodFountain is not null && snapshot.BloodFountain is { } fountain)
        {
            _bloodFountain.RestoreState(fountain);
            _parameterSets[0] = _parameterSets[0] with { Emission = fountain.Emission };
        }
        if (_emperor is not null && snapshot.Emperor is { } emperor)
        {
            _emperor.RestoreState(emperor);
            _parameterSets[0] = _parameterSets[0] with { Emission = _emperor.Emission };
        }
        _birthSizeFactor = snapshot.BirthSizeFactor;
        if (snapshot.Aura is { } aura) _aura.Restore(aura);
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
        if (placement.Definition.BirthSizeRamp is { } ramp)
        {
            _parameterSets[0] = ramp.Apply(_parameterSets[0], _birthSizeFactor);
            _batches[0].ApplyParameters(_parameterSets[0]);
        }
        for (var set = 0; set < _batches.Length; set++)
        foreach (var particle in snapshot.Particles[set]) { _batches[set].Add(particle); _particleCount++; }
    }
}
