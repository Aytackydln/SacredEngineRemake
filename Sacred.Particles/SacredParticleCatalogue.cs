using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Sacred.Particles.Generated;

namespace Sacred.Particles;

/// <summary>Particle catalogue embedded during preprocessing. Loading this data performs
/// no file access, executable decoding or instruction evaluation.</summary>
public sealed class SacredParticleCatalogue
{
    private static readonly Lazy<SacredParticleCatalogue> Low = new(EmbeddedParticleCatalogueLow.Create);
    private static readonly Lazy<SacredParticleCatalogue> Medium = new(EmbeddedParticleCatalogueMedium.Create);
    private static readonly Lazy<SacredParticleCatalogue> High = new(EmbeddedParticleCatalogueHigh.Create);
    private readonly FrozenDictionary<uint, SacredParticleDefinition> _definitions;

    public IReadOnlyCollection<SacredParticleDefinition> Definitions => _definitions.Values;
    public string ExecutableSha256 { get; }
    public string NativeCodeSha256 { get; }
    public SacredParticleQuality Quality { get; }
    public float WorldUnitsPerTile { get; }
    public int DecodedCount { get; }
    public SacredParticleProjection Projection { get; internal init; } = new(1, 1, 1, 1, 0);

    internal SacredParticleCatalogue(string executableHash, string codeHash, SacredParticleQuality quality,
        float worldUnitsPerTile, IEnumerable<SacredParticleDefinition> definitions)
    {
        _definitions = definitions.ToFrozenDictionary(d => d.TypeId);
        ExecutableSha256 = executableHash;
        NativeCodeSha256 = codeHash;
        Quality = quality;
        WorldUnitsPerTile = worldUnitsPerTile;
        DecodedCount = _definitions.Values.Count(d => d.Status == SacredParticleDefinitionStatus.Decoded);
    }

    public static SacredParticleCatalogue LoadEmbedded(SacredParticleQuality quality = SacredParticleQuality.High) => quality switch
    {
        SacredParticleQuality.Low => Low.Value,
        SacredParticleQuality.Medium => Medium.Value,
        SacredParticleQuality.High => High.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };

    public bool TryGetDefinition(uint scriptTypeId, [NotNullWhen(true)] out SacredParticleDefinition? definition) =>
        _definitions.TryGetValue(scriptTypeId, out definition);
}
