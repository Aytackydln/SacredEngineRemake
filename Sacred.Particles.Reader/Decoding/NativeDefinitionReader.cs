using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal sealed class NativeDefinitionReader(SacredExecutableImage image, SacredParticleCatalogueOptions options)
{
    private readonly NativeCode _code = new(image);
    private readonly Dictionary<NativeParticleFamily, IReadOnlyList<SacredParticleTextureBinding>> _bindings = [];

    public SacredParticleDefinition Read(uint typeId, string name, uint recordAddress, CancellationToken cancellationToken)
    {
        var dispatcher = new X86Machine(_code, new PresetMemory(image, 0, options.Quality));
        var factory = dispatcher.ResolveDispatch(SacredGoldExecutableProfile.FactoryDispatchAddress, typeId,
            instruction => instruction.IP < SacredGoldExecutableProfile.FactoryDispatchAddress || instruction.IP >= 0x5A0C99);
        var family = NativeParticleFamily.Find(factory);
        var result = new SacredParticleDefinition(typeId, name, recordAddress, factory, family?.Name, null,
            SacredParticleDefinitionStatus.UnsupportedFamily, "Native family has not been mapped yet.", [], null, []);
        if (family == null) return result;
        try
        {
            var creation = dispatcher.ResolveDispatch(SacredGoldExecutableProfile.CreationDispatchAddress, typeId);
            result = result with { Preset = NativePresetReader.ReadSelector(_code, creation) };
        }
        catch (NotSupportedException exception)
        {
            return result with { Status = SacredParticleDefinitionStatus.UnsupportedCreation, Diagnostic = exception.Message };
        }

        try
        {
            if (!_bindings.TryGetValue(family, out var bindings))
            {
                bindings = NativeTextureReader.ReadBindings(image, _code, family);
                _bindings.Add(family, bindings);
            }
            result = result with { TextureBindings = bindings };
            var draw = NativeTextureReader.ReadDraw(image, _code, family, result.Preset!.Value, bindings);
            result = result with { Draw = draw };
            var parameters = NativePresetReader.Read(image, _code, family, result.Preset.Value, options.Quality, cancellationToken);
            return result with
            {
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, ParameterSets = parameters,
                EmissionMode = NativeSimulationReader.ReadMode(image, _code, family, result.Preset.Value),
                UsesWind = family.ParameterSlotCount > 1
            };
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException)
        {
            return result with { Status = SacredParticleDefinitionStatus.UnsupportedInitializer, Diagnostic = exception.Message };
        }
    }
}
