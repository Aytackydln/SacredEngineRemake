namespace Sacred.Particles;

/// <summary>A combat-move caller's repeated actor bursts, distinct from its named standalone FX layer.
/// Spacing is measured along the dominant axis in native integer world-position units.</summary>
public sealed record SacredParticleCastTrailDefinition(uint TypeId, int Preset, uint Color,
    int WorldStep, uint NativeAddress);
