using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeColumnFlareEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress is not (0x7B7800 or 0x7B9F00)) yield break;
        var panzer = entry.NativeConstructorAddress == 0x7B7800;
        var family = new NativeParticleFamily(panzer ? "Panzer" : "Stahl", entry.FactoryAddress,
            entry.NativeConstructorAddress.Value, panzer ? 0x7B78C5u : 0x7B9FC5u,
            panzer ? 0x7B7B50u : 0x7BA240u, (int)code.At(entry.FactoryAddress).GetImmediate(0), 0, 0, 0, 0)
        {
            InitializerEnd = panzer ? 0x7B7BAEu : 0x7BA2CAu,
            // Explicit standalone inputs: parameter zero base 10, unit scale and level 1, no increments/cap.
            SampleCombatArtDurationBase = 10,
            EventArguments = new Dictionary<int, uint> { [0x40] = 1, [0x70] = BitConverter.SingleToUInt32Bits(1f) }
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        var definition = new SacredColumnFlareFxDefinition(new(bindings.Single().TextureName,
            Immediate(panzer ? 0x7B7A8Du : 0x7BA17Eu), 0, default,
            Operand(panzer ? 0x7B7A58u : 0x7BA149u),
            code.At(panzer ? 0x7B7A64u : 0x7BA155u).GetImmediate(0) == 0,
            code.At(panzer ? 0x7B7A62u : 0x7BA153u).GetImmediate(0) != 0))
        {
            SliceCount = (int)code.At(panzer ? 0x7B7B36u : 0x7BA227u).GetImmediate(1),
            InitialLifetime = BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + 0x20A8, 4)),
            RiseRate = Operand(panzer ? 0x7B7963u : 0x7BA063u), FallRate = Operand(panzer ? 0x7B790Eu : 0x7BA00Eu),
            FadeLeadTime = Operand(panzer ? 0x7B7942u : 0x7BA042u),
            ColorFrequency = Operand(panzer ? 0x7B79AFu : 0x7BA0AFu),
            ColorBias = panzer ? Operand(0x7B79B9) : 0,
            ColorAmplitude = Operand(panzer ? 0x7B79BFu : 0x7BA0B7u),
            ColorOffset = Operand(panzer ? 0x7B79C5u : 0x7BA0BFu), ColorScale = Operand(panzer ? 0x7B79E5u : 0x7BA0D3u),
            BlueChannel = (uint)code.At(panzer ? 0x7B7A25u : 0x7BA113u).GetImmediate(1),
            LightBlue = BitConverter.UInt32BitsToSingle((uint)code.At(panzer ? 0x7B7A2Au : 0x7BA11Bu).GetImmediate(1)),
            GroundAlphaScale = Operand(panzer ? 0x7B7A76u : 0x7BA167u),
            SliceAlphaScale = Operand(panzer ? 0x7B7B12u : 0x7BA203u),
            SliceAlphaFalloff = Operand(panzer ? 0x7B7B00u : 0x7BA1F1u),
            HeightSineBias = Operand(panzer ? 0x7B7AA1u : 0x7BA194u) + Operand(panzer ? 0x7B7AA7u : 0x7BA19Au),
            HeightScale = Operand(panzer ? 0x7B7AADu : 0x7BA1A0u),
            InitialNotification = (uint)code.At(panzer ? 0x7B7BFFu : 0x7BA3A5u).GetImmediate(1),
            InitialPendingCode = panzer ? (uint)code.At(0x7B7BF5).GetImmediate(1) : 0
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, ColumnFlare = definition,
            TextureBindings = bindings, Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = "Complete native 71-plane column; explicit ten-second duration inputs"
        };
    }
}
