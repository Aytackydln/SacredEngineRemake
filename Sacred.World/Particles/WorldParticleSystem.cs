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
    private int _lastLoggedEmitterCount = -1;

    public bool Enabled { get; set; } = true;
    public SacredParticleQuality Quality => _catalogue.Quality;
    public IReadOnlyList<WorldParticle> Particles => _particles;
    public int ActiveEmitterCount => _emitters.Count;
    public ulong Revision { get; private set; }

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
        Revision++;
        Console.WriteLine($"World particle quality set to {_catalogue.Quality}.");
    }

    public void Update(float deltaSeconds, VisibleWorld visibleWorld)
    {
        ArgumentNullException.ThrowIfNull(visibleWorld);
        if (!Enabled)
        {
            _visibleWorld = null;
            if (_emitters.Count > 0 || _particles.Count > 0)
            {
                _emitters.Clear();
                _particles.Clear();
                Revision++;
                LogEmitterCount();
            }
            return;
        }

        if (!ReferenceEquals(_visibleWorld, visibleWorld))
        {
            _visibleWorld = visibleWorld;
            SelectVisibleEmitters(visibleWorld);
        }

        var step = Math.Clamp(deltaSeconds, 0.0f, MaximumUpdateStepSeconds);
        _particles.Clear();
        foreach (var emitter in _emitters.Values)
            emitter.Update(step, _particles);
        if (_emitters.Count > 0 || _particles.Count > 0)
            Revision++;
    }

    private void SelectVisibleEmitters(VisibleWorld visibleWorld)
    {
        _neededEmitters.Clear();
        foreach (var sector in visibleWorld.Sectors)
        foreach (var placement in _script.GetPlacements(sector.Coord))
        {
            if (placement.Definition.Status != SacredParticleDefinitionStatus.Decoded ||
                placement.Definition.Draw is null)
            {
                continue;
            }

            _neededEmitters.Add(placement.ScriptOffset);
            if (_emitters.ContainsKey(placement.ScriptOffset))
                continue;

            var emitter = new WorldParticleEmitter(placement, _worldUnitsPerTile, _catalogue.Projection);
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
