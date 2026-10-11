using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeFireCircleEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x775930) yield break;
        var family = new NativeParticleFamily("cParticleSystem_firecircle", entry.FactoryAddress,
            0x775930, 0x775A56, 0x775DF0, 0x25BC, 0x24C4, 0x24E4, 1, 0x775C05)
        {
            ConstructorState = (0x775965, 0x775A00), ColorOffset = 0x20C4,
            EmitBeforeMovement = false, SampleCombatArtDurationBase = 1000,
            EventArguments = new Dictionary<int, uint> { [0x38] = 1, [0x40] = 1, [0x70] = BitConverter.SingleToUInt32Bits(.01f) }
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        for (var variant = 0; variant < 2; variant++)
        {
            var input = family with { InitialState = new Dictionary<int, uint> { [0xA9] = (uint)variant } };
            var memory = NativePresetReader.Evaluate(image, code, input, 0, quality, token);
            var textureOffset = (uint)(variant == 0 ? code.At(0x775BD2).MemoryDisplacement64 : code.At(0x775BDB).MemoryDisplacement64);
            var draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single(b => b.NativeHandleOffset == textureOffset));
            yield return entry with
            {
                NativeClass = family.Name, Preset = variant, IsEventPreset = true, OneTime = true,
                DisplayName = variant == 0 ? "Fire02; sampled combat duration 10 s" : "Fire09; native duration 3 s",
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                TextureBindings = bindings, Draw = draw, ParameterSets = NativePresetReader.ReadParameters(memory, family),
                Capacity = capacity, EmitBeforeMovement = false, SingleBirthInitialization = true,
                EmissionDurationSeconds = BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + 0x20A4, 4))
            };
        }
    }
}
