using System.Numerics;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Control-only world playback. No CPU live state or lifetime mirror.</summary>
public sealed class WorldGpuEmitter
{
    private static long _nextIdentity;
    private readonly List<WorldGpuEmitterStep> _pending = [];
    private ulong _sequence;
    public WorldGpuEmitter(WorldParticleScriptPlacement placement, SacredParticleProjection projection,
        WorldParticleEmissionSnapshot? initial = null)
    {
        Placement = placement; Projection = projection;
        Parameters = placement.Definition.ParameterSets.Where(p => p.Emission.Size > 0 &&
            (p.Emission.EmissionInterval > 0 || p.Emission.BurstCount > 0) ||
            placement.Definition.InitialParticles.Count > 0 || placement.Definition.ModelBurstCount > 0).ToArray();
        Initial = initial;
        Handle = new(Interlocked.Increment(ref _nextIdentity), 1);
        var draw = placement.Definition.Draw!;
        Sprite = new(draw.TextureName, draw.AtlasSide, draw.AtlasSide, checked(draw.AtlasSide * draw.AtlasSide),
            1, ParticleShaderKind.ItemParticle);
    }
    public ParticleControlHandle Handle { get; }
    public WorldParticleScriptPlacement Placement { get; }
    public SacredParticleDefinition Definition => Placement.Definition;
    public SacredParticleProjection Projection { get; }
    public SacredParticleParameterSet[] Parameters { get; }
    public ParticleSpriteReference Sprite { get; }
    public WorldParticleEmissionSnapshot? Initial { get; }
    public Vector2 Origin => new(Placement.WorldX, Placement.WorldY);
    public Vector2 DepthAnchor => Placement.Creation.TilePosition is { } tile ? new(tile.X, tile.Y) : Origin;
    public float HeightOffset => Placement.Creation.HeightOffset ?? 0;
    public int PendingCount => _pending.Count;
    public IReadOnlyList<WorldGpuEmitterStep> Pending => _pending;
    public void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt is < 0 or > .1f) throw new ArgumentOutOfRangeException(nameof(dt));
        _pending.Add(new(checked(++_sequence), dt));
    }
    public void Acknowledge(ulong sequence) => _pending.RemoveAll(step => step.Sequence <= sequence);
}

public readonly record struct WorldGpuEmitterStep(ulong Sequence, float Seconds);
