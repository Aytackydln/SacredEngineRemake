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
    private readonly HashSet<int> _neededEmitters = [];
    private readonly List<int> _emittersToRemove = [];
    private readonly List<WorldParticle> _particles = new(2048);
    private WorldParticleScriptIndex _script;
    private SacredParticleCatalogue _catalogue;
    private float _worldUnitsPerTile;
    private VisibleWorld? _visibleWorld;
    private IndoorTileGroup? _activeIndoorGroup;
    private int _lastLoggedEmitterCount = -1;

    private readonly List<WorldGpuParticleBatch> _gpuBatches = [];
    public IReadOnlyList<WorldGpuParticleBatch> GpuBatches => _gpuBatches;
    public IParticleGpuBackend? GpuBackend { get; set; }
    public bool Enabled { get; set; } = true;
    public SacredParticleQuality Quality => _catalogue.Quality;
    public ParticleSimulationMode SimulationMode { get; private set; } = ParticleSimulationMode.Auto;
    public ParticleSimulationMode SelectedSimulationMode { get; private set; } = ParticleSimulationMode.CpuSimd;
    public IReadOnlyList<WorldParticle> Particles => _particles;
    public int ActiveEmitterCount => _emitters.Count;
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
        if (changed)
        {
            Effects.SetSimulationMode(selected);
            foreach (var emitter in _emitters.Values) emitter.SimulationMode = selected;
            SelectedSimulationMode = selected;
        }
        if (changed || forceLog)
            Console.WriteLine($"World particle backend: requested {SimulationMode}; selected {selected}; " +
                $"{(selected == ParticleSimulationMode.CpuSimd && gpuRequested ? "GPU renderer/pipelines unavailable; " : string.Empty)}SIMD accelerated: {Vector.IsHardwareAccelerated}.");
    }

    public void Update(float deltaSeconds, VisibleWorld visibleWorld, IndoorTileGroup? activeIndoorGroup = null,
        Vector2 selfPosition = default, float selfHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(visibleWorld);
        SelectSimulationBackend();
        _gpuBatches.Clear();
        Effects.GpuBackend = GpuBackend;
        if (!Enabled)
        {
            _visibleWorld = null;
            if (_emitters.Count > 0 || _particles.Count > 0 || Effects.ActiveCount > 0)
            {
                _emitters.Clear();
                _particles.Clear();
                Effects.Clear();
                Revision++;
                LogEmitterCount();
            }
            return;
        }

        if (!ReferenceEquals(_visibleWorld, visibleWorld) || _activeIndoorGroup?.Id != activeIndoorGroup?.Id)
        {
            _visibleWorld = visibleWorld;
            _activeIndoorGroup = activeIndoorGroup;
            SelectVisibleEmitters(visibleWorld);
        }

        var step = float.IsFinite(deltaSeconds) ? Math.Clamp(deltaSeconds, 0.0f, MaximumUpdateStepSeconds) : 0;
        var hadParticles = _particles.Count > 0;
        _particles.Clear();
        foreach (var emitter in _emitters.Values)
        {
            emitter.GpuBackend = GpuBackend;
            emitter.Update(step, _particles);
            emitter.CollectGpuBatches(_gpuBatches);
        }
        Effects.Update(step, selfPosition, selfHeight, SelectedSimulationMode, _particles);
        Effects.CollectGpuBatches(_gpuBatches);
        if (hadParticles || _emitters.Count > 0 || _particles.Count > 0 || _gpuBatches.Count > 0)
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
            if (_emitters.ContainsKey(placement.ScriptOffset))
                continue;

            var emitter = new WorldParticleEmitter(placement, _worldUnitsPerTile, _catalogue.Projection);
            emitter.SimulationMode = SelectedSimulationMode;
            if (emitter.CanEmit)
                _emitters.Add(placement.ScriptOffset, emitter);
        }

        _emittersToRemove.Clear();
        foreach (var offset in _emitters.Keys)
            if (!_neededEmitters.Contains(offset))
                _emittersToRemove.Add(offset);
        foreach (var offset in _emittersToRemove)
            _emitters.Remove(offset);

        LogEmitterCount();
    }

    private void LogEmitterCount()
    {
        if (_lastLoggedEmitterCount == _emitters.Count)
            return;
        _lastLoggedEmitterCount = _emitters.Count;
        Console.WriteLine($"World particle emitters active: {_emitters.Count:N0}.");
    }
}
