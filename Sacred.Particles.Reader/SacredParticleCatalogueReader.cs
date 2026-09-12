using System.Diagnostics;
using Sacred.Particles.Reader.Decoding;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader;

/// <summary>Preprocesses the original executable into catalogue data. Used by the
/// generation command; game clients use SacredParticleCatalogue.LoadEmbedded.</summary>
public static class SacredParticleCatalogueReader
{
    public static SacredParticleCatalogue Load(string executablePath, SacredParticleCatalogueOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        options ??= new SacredParticleCatalogueOptions();
        if (options.Quality is < SacredParticleQuality.Low or > SacredParticleQuality.High)
            throw new ArgumentOutOfRangeException(nameof(options), "Particle quality must be Low, Medium or High.");
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        options.Log?.Invoke($"[particles] Reading executable catalogue: {executablePath}");
        var image = SacredExecutableImage.Load(executablePath);
        var reader = new NativeDefinitionReader(image, options);
        var definitions = new List<SacredParticleDefinition>();
        for (var index = 0; index < SacredGoldExecutableProfile.TypeTableCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = SacredGoldExecutableProfile.TypeTableAddress + (uint)(index * SacredExecutableTypeNameLayout.SerializedSize);
            var typeId = image.UInt32(address);
            var name = image.String(address + 4, SacredExecutableTypeNameLayout.NameLength);
            if (!name.StartsWith("TYPE_FX_", StringComparison.Ordinal)) continue;
            definitions.Add(reader.Read(typeId, name, address, cancellationToken));
        }
        var catalogue = new SacredParticleCatalogue(image.ExecutableSha256, image.CodeSha256, options.Quality,
            image.Single(SacredGoldExecutableProfile.WorldUnitsPerTileAddress), definitions)
        {
            Projection = NativeProjectionReader.Read(image)
        };
        options.Log?.Invoke($"[particles] Catalogue loaded: {definitions.Count} FX types, {catalogue.DecodedCount} decoded, " +
                            $"{definitions.Count - catalogue.DecodedCount} pending mappings; quality={options.Quality}; {timer.ElapsedMilliseconds} ms.");
        return catalogue;
    }
}
