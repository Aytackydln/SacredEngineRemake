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
    /// <summary>stdRender half-size multiplier, applied before CENTER_DOWN and projection.</summary>
    public float HalfSizeMultiplier { get; init; } = 1;
    public bool UsesColorTable => (RawFlags & 0x08) != 0;
    public bool UsesRandomAtlasCell => (RawFlags & 0x100) != 0;
    public bool UsesParticleColor => (RawFlags & 0x20) != 0;
    public bool UsesRotation => (RawFlags & (uint)SacredParticleRenderMode.Phi) != 0;
    /// <summary>Native CENTER_DOWN: the quad extends from local Y=0 to twice its half-size.</summary>
    public bool AnchoredAtBottom => (RawFlags & (uint)SacredParticleRenderMode.CenterAtDown) != 0;
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
    public string? DisplayName { get; init; }
    /// <summary>Constructor reached by the native factory; equal addresses identify a shared FX family.</summary>
    public uint? NativeConstructorAddress { get; init; }
    /// <summary>Finite native emission window; EmissionClock determines the tested age and boundary rule.</summary>
    public float? EmissionDurationSeconds { get; init; }
    public SacredParticleEmissionClock EmissionClock { get; init; }
    /// <summary>stdCreation initializes a zero release clock with one birth after movement has built its free-slot list.</summary>
    public bool SingleBirthInitialization { get; init; }
    /// <summary>Native stdCreationFromActor vertex limit; zero means no actor mesh input.</summary>
    public int ModelBurstCount { get; init; }
    /// <summary>Native owner/bone attachment requires actor context even without vertex-seeded births.</summary>
    public bool RequiresActorContext { get; init; }
    public bool RequiresActor => RequiresActorContext || ModelBurstCount > 0;
    /// <summary>Native actor radius scales emission size/gravity and motion size change.
    /// Stored parameters are evaluated at radius one; playback requires the actor's Items.pak field.</summary>
    public bool UsesActorBlockRadius { get; init; }
    public SacredParticleHaloDefinition? Halo { get; init; }
    /// <summary>Further native stdRender passes over the same particle vector, in draw order.</summary>
    public IReadOnlyList<SacredParticleDrawDefinition> AdditionalDraws { get; init; } = [];
    public IReadOnlyList<SacredParticleHaloDefinition> AdditionalHalos { get; init; } = [];
    /// <summary>The native active byte ends the whole effect at its duration, including surviving particles.</summary>
    public bool RetireAfterDuration { get; init; }
    /// <summary>Some native quality branches simulate the vector but draw only a lensflare.</summary>
    public bool ParticlesVisible { get; init; } = true;
    /// <summary>Preset is an event subtype rather than a script creation selector.</summary>
    public bool IsEventPreset { get; init; }
    /// <summary>The native effect runs once and drains, through a finite burst or a timed emission window.</summary>
    public bool OneTime { get; init; }
    /// <summary>Native advanceTime emits the point burst on its first call, preserving birth/movement order.</summary>
    public bool BurstOnFirstUpdate { get; init; }
    /// <summary>Laser-impact advanceTime creates its burst in place and starts movement on the next call.</summary>
    public bool SkipFirstMovement { get; init; }
    /// <summary>Maximum particle vector count requested by the native factory.</summary>
    public int Capacity { get; init; } = 200;
    public IReadOnlyList<SacredNativeParticleLayout> InitialParticles { get; init; } = [];
    /// <summary>0x7687F0 selection mode: 1 single, 2 80/20, 3 inverse-interval weights.</summary>
    public int EmissionMode { get; init; } = 1;
    /// <summary>Whether the native update generates births before integrating existing particles.</summary>
    public bool EmitBeforeMovement { get; init; } = true;
    public bool UsesWind { get; init; }
    public SacredParticleEmissionCycle? EmissionCycle { get; init; }
    public SacredParticleLineEmissionDefinition? LineEmission { get; init; }
    public SacredParticleStrengthDefinition? Strength { get; init; }
    /// <summary>Custom orbit/ejection simulation; no standard parameter blocks.</summary>
    public SacredTeleportParticleDefinition? Teleport { get; init; }
    public SacredParticleOrbitEmission? OrbitEmission { get; init; }
    /// <summary>Selected by the native movement routine, not by texture or item IDs.
    /// Decoded smoke/dwarf-magic use stdMovement and therefore have no ground collision.</summary>
    public ParticleGroundCollision GroundCollision { get; init; }
}
