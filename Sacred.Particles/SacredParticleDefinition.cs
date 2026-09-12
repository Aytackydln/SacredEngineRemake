using Sacred.Particles.Particles;

namespace Sacred.Particles;

public enum SacredParticleDefinitionStatus
{
    Decoded,
    UnsupportedFamily,
    UnsupportedCreation,
    UnsupportedInitializer
}

/// <summary>A parameter block written by native initialization. A written zero block is
/// retained; its existence does not prove that the native generator selects it.</summary>
public sealed record SacredParticleParameterSet(int Index,
    SacredParticleEmissionLayout Emission, SacredParticleMotionLayout Motion)
{
    /// <summary>Original packed AARRGGBB values: 256 fade entries, four fixed
    /// corner colors, or empty when the initializer did not write this table.</summary>
    public IReadOnlyList<uint> Colors { get; init; } = Array.Empty<uint>();
}

public sealed record SacredParticleTextureBinding(uint NativeHandleOffset, string TextureName);

/// <summary>Recovered render arguments; RawFlags still require native draw-path interpretation.</summary>
public sealed record SacredParticleDrawDefinition(string TextureName, uint RawFlags, uint NativeAddress)
{
    public int AtlasSide { get; init; } = 1;
    public bool UsesColorTable => (RawFlags & 0x08) != 0;
    public bool UsesRandomAtlasCell => (RawFlags & 0x100) != 0;
}

/// <summary>One native TYPE_FX catalogue entry, keyed by the same ID used in script tag 0x02.
/// Unsupported entries carry metadata/diagnostics and never fabricated particle parameters.</summary>
public sealed record SacredParticleDefinition(
    uint TypeId,
    string TypeName,
    uint TypeRecordAddress,
    uint FactoryAddress,
    string? NativeClass,
    int? Preset,
    SacredParticleDefinitionStatus Status,
    string? Diagnostic,
    IReadOnlyList<SacredParticleTextureBinding> TextureBindings,
    SacredParticleDrawDefinition? Draw,
    IReadOnlyList<SacredParticleParameterSet> ParameterSets)
{
    /// <summary>0x7687F0 selection mode: 1 single, 2 80/20, 3 inverse-interval weights.</summary>
    public int EmissionMode { get; init; } = 1;
    public bool UsesWind { get; init; }
}
