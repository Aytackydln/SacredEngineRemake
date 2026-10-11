using System.Numerics;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeSpriteFlareEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var constructor = entry.NativeConstructorAddress;
        if (constructor is not (0x77F0F0 or 0x7A20E0 or 0x7A23F0)) yield break;
        var bfg = constructor == 0x77F0F0; var pill = constructor == 0x7A20E0;
        var family = new NativeParticleFamily(bfg ? "BFG" : pill ? "Trap02 Pill" : "Trap02 Ghost",
            entry.FactoryAddress, constructor.Value, bfg ? 0x77F1A8u : pill ? 0x7A2153u : 0x7A2463u,
            bfg ? 0x77F520u : pill ? 0x7A23D0u : 0x7A26D0u, (int)code.At(entry.FactoryAddress).GetImmediate(0), 0, 0, 0, 0)
        { InitializerEnd = bfg ? 0x77F580u : null, InitialState = new Dictionary<int, uint> { [0xAC] = 0 } };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        uint State(uint offset) => (uint)memory.Integer(memory.BaseAddress + offset, 4);
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        var layers = new List<SacredFxFlareLayer>();
        if (bfg)
            foreach (var offset in new uint[] { 0x20B8, 0x20BC, 0x20C0 })
                layers.Add(new(Texture(offset), Immediate(0x77F2DF), 0, default, 0, true, true));
        else layers.Add(new(Texture(0x20A8), Immediate(pill ? 0x7A21DBu : 0x7A24DBu),
            (uint)code.At(pill ? 0x7A21CCu : 0x7A24CCu).GetImmediate(0),
            new Vector3(0, 0, BitConverter.UInt32BitsToSingle((uint)code.At(pill ? 0x7A21F3u : 0x7A24F3u).GetImmediate(1))),
            0, code.At(pill ? 0x7A21D1u : 0x7A24D1u).GetImmediate(0) == 0xFF, true));
        var definition = new SacredSpriteFlareFxDefinition(layers.AsReadOnly())
        {
            GroundFlare = bfg ? new(Texture(0x20B4), Immediate(0x77F343), (uint)code.At(0x77F33E).GetImmediate(0), default, 0, true, true) : null,
            OscillatesColors = bfg, Lifetime = bfg ? Operand(0x77F1CF) : 0,
            ColorFrequency = bfg ? Operand(0x77F207) : 0, ColorAmplitude = bfg ? Operand(0x77F217) : 0,
            ColorBase = bfg ? unchecked((uint)code.At(0x77F242).GetImmediate(1) << 16) : 0,
            ComplementIntensity = bfg ? (uint)code.At(0x77F226).GetImmediate(1) : 0,
            PulseThreshold = bfg ? 0 : image.Single(0x8907E4), PulseRiseScale = bfg ? 0 : image.Single(0x897858),
            PulseFallScale = bfg ? 0 : image.Single(0x897854), PulseAmplitude = bfg ? 0 : Immediate(pill ? 0x7A21D6u : 0x7A24D6u),
            RemovalFlag = bfg ? (ushort)code.At(0x77F1EF).GetImmediate(1) : pill ? (ushort)code.At(0x7A217E).GetImmediate(1) : (ushort)0,
            InitialNotification = State(0x7C), InitialLightStrength = State(0x9C), InitialAuxiliary = State(0x98)
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = bfg,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, SpriteFlare = definition,
            TextureBindings = bindings, Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = bfg ? "Three animated billboard layers and native ground glow" : "Native asymmetric size pulse"
        };
    }
}
