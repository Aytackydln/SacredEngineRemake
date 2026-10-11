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
        var events = new List<SacredParticleDefinition>();
        var code = new NativeCode(image);
        for (var index = 0; index < SacredGoldExecutableProfile.TypeTableCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = SacredGoldExecutableProfile.TypeTableAddress + (uint)(index * SacredExecutableTypeNameLayout.SerializedSize);
            var typeId = image.UInt32(address);
            var name = image.String(address + 4, SacredExecutableTypeNameLayout.NameLength);
            if (!name.StartsWith("TYPE_FX_", StringComparison.Ordinal)) continue;
            var definition = reader.Read(typeId, name, address, cancellationToken);
            definitions.Add(definition);
            events.AddRange(NativeMineSignEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeBloodFountainEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeEmperorEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeGenericEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeChangelingEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeParticleEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativePointBurstReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeTimedEmissionReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeStandardEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeHitAndGroundEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeAuraAndCastEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeSustainedAndBurstEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeLayeredEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeSmallFireEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeLineEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeAnducarEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeDoorSpikeEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeStonedEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativePlongEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeLayeredFlareEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeStrikeSymbolEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeShieldedEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeRingShieldEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeSuperfluidEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeConfusionEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeFireCircleEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeHealingEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativePulseFlareEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeWarCrySignEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeExpandingBurstEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeBubbleTrapExplosionEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeTerrorSprinterEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeColumnFlareEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeSpriteFlareEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeControlledFlareEventReader.Read(image, code, definition, options.Quality, cancellationToken));
            events.AddRange(NativeTeleportEventReader.Read(image, code, definition, options.Quality, cancellationToken));
        }
        var catalogue = new SacredParticleCatalogue(image.ExecutableSha256, image.CodeSha256, options.Quality,
            image.Single(SacredGoldExecutableProfile.WorldUnitsPerTileAddress), definitions, events)
        {
            Projection = NativeProjectionReader.Read(image)
        };
        options.Log?.Invoke($"[particles] Catalogue loaded: {definitions.Count} FX types, {catalogue.DecodedCount} decoded script presets, {events.Count} decoded event variants, " +
                            $"{definitions.Count - catalogue.DecodedCount} pending script-creation mappings; quality={options.Quality}; {timer.ElapsedMilliseconds} ms.");
        return catalogue;
    }
}
