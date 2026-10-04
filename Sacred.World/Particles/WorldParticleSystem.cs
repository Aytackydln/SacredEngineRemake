using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>
/// Activates decoded particle declarations for loaded sectors and advances their
/// recovered emission and motion parameters.
/// </summary>
public sealed class WorldParticleSystem
{
    private const float MaximumUpdateStepSeconds = 0.1f;

    private readonly Dictionary<int, WorldParticleEmitter> _emitters = [];
    private readonly Dictionary<int, WorldGpuEmitter> _gpuEmitters = [];
    private readonly List<WorldGpuEmitter> _gpuEmitterInputs = [];
    public IReadOnlyList<WorldGpuEmitter> GpuEmitters => _gpuEmitterInputs;
    public bool GpuEmissionEnabled { get; private set; }
    public float? FixedStepSeconds { get; private set; }
    public void SetFixedStep(float? seconds)
    {
        if (seconds is { } value && (!float.IsFinite(value) || value is < 0 or > MaximumUpdateStepSeconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        FixedStepSeconds = seconds;
    }
    public IWorldParticleEmissionBackend? EmissionBackend { get; set; }
    public void SetGpuEmission(bool enabled)
    {
        if (enabled == GpuEmissionEnabled) return;
        if (enabled)
        {
            var converted = _emitters.ToDictionary(p => p.Key, p =>
                new WorldGpuEmitter(p.Value.Placement, _catalogue.Projection, p.Value.Capture()));
            foreach (var p in converted) _gpuEmitters.Add(p.Key, p.Value);
            _emitters.Clear();
        }
        else
        {
            var backend = EmissionBackend ?? throw new InvalidOperationException("Emission snapshot backend unavailable.");
            var converted = _gpuEmitters.ToDictionary(p => p.Key, p => RestoreEmitter(p.Value, backend));
            foreach (var p in converted) { p.Value.GpuBackend = GpuBackend; p.Value.SimulationMode = SelectedSimulationMode; _emitters.Add(p.Key, p.Value); }
            _gpuEmitters.Clear(); _gpuEmitterInputs.Clear();
        }
        GpuEmissionEnabled = enabled; _visibleWorld = null;
        Console.WriteLine($"World-object GPU emission {(enabled ? "enabled" : "disabled; current state explicitly restored")}; preview/equipment backends unchanged.");
    }
    private WorldParticleEmitter RestoreEmitter(WorldGpuEmitter emitter, IWorldParticleEmissionBackend backend)
    {
        var snapshot = backend.HasState(emitter) ? backend.Snapshot(emitter) : emitter.Initial;
        var cpu = snapshot is null ? new WorldParticleEmitter(emitter.Placement, _worldUnitsPerTile, _catalogue.Projection) :
            new WorldParticleEmitter(emitter.Placement, _worldUnitsPerTile, _catalogue.Projection, snapshot);
        // Explicit restoration can execute retained controls when compute pipelines failed.
        var discardedDraws = new List<WorldParticle>();
        foreach (var step in emitter.Pending) { cpu.Update(step.Seconds, discardedDraws); discardedDraws.Clear(); }
        if (emitter.PendingCount > 0) emitter.Acknowledge(emitter.Pending[^1].Sequence);
        return cpu;
    }
    private readonly HashSet<int> _neededEmitters = [];
    private readonly List<int> _emittersToRemove = [];
    private readonly List<WorldParticle> _particles = new(2048);
    private WorldParticleScriptIndex _script;
    private SacredParticleCatalogue _catalogue;
    private float _worldUnitsPerTile;
    private VisibleWorld? _visibleWorld;
    private IndoorTileGroup? _activeIndoorGroup;
    private int _lastLoggedEmitterCount = -1;
    private bool? _lastGpuAvailable;

    private readonly List<WorldGpuParticleBatch> _gpuBatches = [];
    public IReadOnlyList<WorldGpuParticleBatch> GpuBatches => _gpuBatches;
    public IParticleGpuBackend? GpuBackend { get; set; }
    public bool Enabled { get; set; } = true;
    public SacredParticleQuality Quality => _catalogue.Quality;
    public ParticleSimulationMode SimulationMode { get; private set; } = ParticleSimulationMode.Auto;
    public ParticleSimulationMode SelectedSimulationMode { get; private set; } = ParticleSimulationMode.CpuSimd;
    public IReadOnlyList<WorldParticle> Particles => _particles;
    public int ActiveEmitterCount => _emitters.Count + _gpuEmitters.Count;
    public ulong Revision { get; private set; }
    public SacredParticleCatalogue Catalogue => _catalogue;
    public WorldParticleEffectPlayer Effects { get; } = new();

    public WorldParticleSystem(
        WorldParticleScriptIndex script,
        SacredParticleQuality quality = SacredParticleQuality.High)
    {
        ArgumentNullException.ThrowIfNull(script);
        _script = script;
        _catalogue = SacredParticleCatalogue.LoadEmbedded(quality);
        _worldUnitsPerTile = _catalogue.WorldUnitsPerTile;
        if (_catalogue.Quality != SacredParticleQuality.High && !string.IsNullOrEmpty(script.SourcePath))
            _script = WorldParticleScriptIndex.Load(script.SourcePath, _catalogue);
    }

    public void SetQuality(SacredParticleQuality quality)
    {
        if (_catalogue.Quality == quality)
            return;

        _catalogue = SacredParticleCatalogue.LoadEmbedded(quality);
        _worldUnitsPerTile = _catalogue.WorldUnitsPerTile;
        _script = string.IsNullOrEmpty(_script.SourcePath)
            ? _script
            : WorldParticleScriptIndex.Load(_script.SourcePath, _catalogue);
        _visibleWorld = null;
        _emitters.Clear();
        _gpuEmitters.Clear(); _gpuEmitterInputs.Clear();
        _particles.Clear();
        _gpuBatches.Clear();
        Effects.Clear();
        Revision++;
        Console.WriteLine($"World particle quality set to {_catalogue.Quality}.");
    }

    public void SetSimulationMode(ParticleSimulationMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        SimulationMode = mode;
        SelectSimulationBackend(forceLog: true);
    }

    private void SelectSimulationBackend(bool forceLog = false)
    {
        var gpuRequested = SimulationMode is ParticleSimulationMode.Auto or ParticleSimulationMode.Gpu;
        var selected = gpuRequested
            ? GpuBackend is { IsAvailable: true } ? ParticleSimulationMode.Gpu : ParticleSimulationMode.CpuSimd
            : SimulationMode;
        var changed = selected != SelectedSimulationMode;
        var available = GpuBackend is { IsAvailable: true };
        var availabilityChanged = _lastGpuAvailable != available;
        _lastGpuAvailable = available;
        if (changed)
        {
            Effects.SetSimulationMode(selected);
            foreach (var emitter in _emitters.Values) emitter.SimulationMode = selected;
            SelectedSimulationMode = selected;
        }
        if (changed || forceLog || (selected == ParticleSimulationMode.GpuOnly && availabilityChanged))
            Console.WriteLine($"World particle backend: requested {SimulationMode}; selected {selected}; " +
                $"{(selected == ParticleSimulationMode.GpuOnly ? $"CPU fallback disabled; GPU {(GpuBackend is { IsAvailable: true } ? "ready" : "unavailable, playback paused")}; " : string.Empty)}" +
                $"{(selected == ParticleSimulationMode.CpuSimd && gpuRequested ? "GPU renderer/pipelines unavailable; " : string.Empty)}SIMD accelerated: {Vector.IsHardwareAccelerated}.");
    }

    public void Update(float deltaSeconds, VisibleWorld visibleWorld, IndoorTileGroup? activeIndoorGroup = null,
        Vector2 selfPosition = default, float selfHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(visibleWorld);
        SelectSimulationBackend();
        _gpuBatches.Clear();
        _gpuEmitterInputs.Clear();
        Effects.GpuBackend = GpuBackend;
        if (!Enabled)
        {
            _visibleWorld = null;
            if (_emitters.Count > 0 || _gpuEmitters.Count > 0 || _particles.Count > 0 || Effects.ActiveCount > 0)
            {
                _emitters.Clear();
                _gpuEmitters.Clear();
                _particles.Clear();
                Effects.Clear();
                Revision++;
                LogEmitterCount();
            }
            return;
        }

        if (SelectedSimulationMode == ParticleSimulationMode.GpuOnly && GpuBackend is not { IsAvailable: true })
        {
            if (_particles.Count > 0) { _particles.Clear(); Revision++; }
            // Keep renderer ownership of persistent state while its pipelines are unavailable.
            foreach (var emitter in _emitters.Values) emitter.CollectGpuBatches(_gpuBatches);
            Effects.CollectGpuBatches(_gpuBatches);
            _gpuEmitterInputs.AddRange(_gpuEmitters.Values);
            return;
        }

        if (!ReferenceEquals(_visibleWorld, visibleWorld) || _activeIndoorGroup?.Id != activeIndoorGroup?.Id)
        {
            _visibleWorld = visibleWorld;
            _activeIndoorGroup = activeIndoorGroup;
            SelectVisibleEmitters(visibleWorld);
        }

        var step = FixedStepSeconds ?? (float.IsFinite(deltaSeconds) ? Math.Clamp(deltaSeconds, 0.0f, MaximumUpdateStepSeconds) : 0);
        var hadParticles = _particles.Count > 0;
        _particles.Clear();
        foreach (var emitter in _emitters.Values)
        {
            emitter.GpuBackend = GpuBackend;
            emitter.Update(step, _particles);
            emitter.CollectGpuBatches(_gpuBatches);
        }
        foreach (var emitter in _gpuEmitters.Values)
        {
            if (EmissionBackend?.IsReady(emitter) == true) emitter.Step(step);
            _gpuEmitterInputs.Add(emitter);
        }
        Effects.Update(step, selfPosition, selfHeight, SelectedSimulationMode, _particles);
        Effects.CollectGpuBatches(_gpuBatches);
        if (hadParticles || ActiveEmitterCount > 0 || _particles.Count > 0 || _gpuBatches.Count > 0)
            Revision++;
    }

    private void SelectVisibleEmitters(VisibleWorld visibleWorld)
    {
        _neededEmitters.Clear();
        var groups = visibleWorld.Sectors.SelectMany(static sector => sector.IndoorTileGroups.Groups)
            .DistinctBy(static group => group.Id).ToArray();
        foreach (var sector in visibleWorld.Sectors)
        foreach (var placement in _script.GetPlacements(sector.Coord))
        {
            if (placement.Definition.Status != SacredParticleDefinitionStatus.Decoded ||
                placement.Definition.Draw is null ||
                !WorldParticleSurfaceVisibility.IsVisible(placement, groups, _activeIndoorGroup))
            {
                continue;
            }

            _neededEmitters.Add(placement.ScriptOffset);
            if (_emitters.ContainsKey(placement.ScriptOffset) || _gpuEmitters.ContainsKey(placement.ScriptOffset))
                continue;

            if (GpuEmissionEnabled)
            {
                var gpu = new WorldGpuEmitter(placement, _catalogue.Projection);
                if (gpu.Parameters.Length > 0) _gpuEmitters.Add(placement.ScriptOffset, gpu);
                continue;
            }

            var emitter = new WorldParticleEmitter(placement, _worldUnitsPerTile, _catalogue.Projection);
            emitter.SimulationMode = SelectedSimulationMode;
            if (emitter.CanEmit)
                _emitters.Add(placement.ScriptOffset, emitter);
        }

        _emittersToRemove.Clear();
        foreach (var offset in _emitters.Keys)
            if (!_neededEmitters.Contains(offset))
                _emittersToRemove.Add(offset);
        foreach (var offset in _gpuEmitters.Keys)
            if (!_neededEmitters.Contains(offset)) _emittersToRemove.Add(offset);
        foreach (var offset in _emittersToRemove)
        {
            _emitters.Remove(offset);
            _gpuEmitters.Remove(offset);
        }

        LogEmitterCount();
    }

    private void LogEmitterCount()
    {
        if (_lastLoggedEmitterCount == ActiveEmitterCount)
            return;
        _lastLoggedEmitterCount = ActiveEmitterCount;
        Console.WriteLine($"World particle emitters active: {ActiveEmitterCount:N0}; GPU emission {_gpuEmitters.Count}.");
    }
}
