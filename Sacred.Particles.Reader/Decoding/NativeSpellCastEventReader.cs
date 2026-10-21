using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Hand effects created by EiMagic. The selector and clock are event inputs.</summary>
internal static class NativeSpellCastEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x776AB0) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var family = new NativeParticleFamily("cParticleSystem_cast_spell", entry.FactoryAddress,
            0x776AB0, 0x776BB0, 0x7773D0, 0x20A0 + SacredSpellCastParticleStateLayout.SerializedSize + 24,
            0x24B8, 0x24D8, 1, 0x776F03)
        {
            ColorOffset = 0x20B8, SelectorOffset = 0x3C,
            InitialState = new Dictionary<int, uint> { [0xA9] = 0 },
            EventArguments = new Dictionary<int, uint> { [0x20] = BitConverter.SingleToUInt32Bits(1) }
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        // EiMagic's playable-class branches: particles, Wood Elf flare and Daemon twirl.
        foreach (var preset in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 16, 17, 18, 19, 20, 22, 24 })
        {
            var (drawAddress, textureOffset) = preset switch
            {
                5 or 6 or 7 or 8 or 24 => (0x776F03u, 0x253Cu),
                9 or 12 => (0x776F4Au, 0x253Cu),
                11 => (0x77701Eu, 0x2548u),
                16 => (0x776D37u, 0x2540u),
                22 => (0x77718Au, 0x2550u),
                _ => (0x776CF0u, 0x2544u)
            };
            var texture = bindings.Single(binding => binding.NativeHandleOffset == textureOffset);
            var draw = preset is 10 or 22 ? new(texture.TextureName, 0, drawAddress) : NativeTextureReader.ReadDraw(image, code,
                family with { DrawAddress = drawAddress }, preset, bindings, texture);
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            float State(uint offset) => BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + offset, 4));
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                DisplayName = "Native spell hand particles; event-scaled emission clock",
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                Capacity = capacity, TextureBindings = bindings, Draw = draw,
                ParameterSets = NativePresetReader.ReadParameters(memory, family),
                AdditionalHalos = NativeSpellCastHaloReader.Read(image, code, preset, State(0x20A8), State(0x20AC), bindings),
                SimulationStartSeconds = State(0x20A8), SimulationStartUsesCurrentTime = true,
                EmissionDurationSeconds = State(0x20AC),
                EmissionClock = SacredParticleEmissionClock.CurrentTimeInclusive,
                SingleBirthInitialization = true, EmitBeforeMovement = false
            };
        }
    }
}
