using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeVampireCastEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7915C0) yield break;
        var family = new NativeParticleFamily("cParticleSystem_vamp_sm_cast", entry.FactoryAddress,
            0x7915C0, 0x7916C8, 0x791FB0, 0x3308, 0x30B8, 0x3138, 3, 0x791D1A)
        { SelectorOffset = 0x3C, ColorOffset = 0x20B8, ColorSlotStride = 0x400,
            InitializerEnd = 0x792774, EmitBeforeMovement = false };
        // Native preset IDs are CA codes. Both form transitions take the same initialization branch.
        var memory = NativePresetReader.Evaluate(image, code, family, 1022, quality, token);
        var sets = NativePresetReader.ReadParameters(memory, family);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        string Texture(uint handle) => bindings.Single(b => b.NativeHandleOffset == handle).TextureName;
        uint Immediate(uint address, int operand = 0) => (uint)code.At(address).GetImmediate(operand);
        float Float(uint address) => BitConverter.UInt32BitsToSingle(Immediate(address));
        yield return entry with
        {
            Preset = 0, IsEventPreset = true, OneTime = true, NativeClass = family.Name,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
            TextureBindings = bindings, ParameterSets = sets, EmitBeforeMovement = false,
            EmissionMode = checked((int)Immediate(0x7919A7, 1)), SingleBirthInitialization = true,
            NativeAttachmentFlags = Immediate(0x792793, 1),
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            Draw = new(Texture(0x32E0), Immediate(0x791CFF), 0x791D1A) { AtlasSide = checked((int)Immediate(0x791CFC)) },
            VampireTransformation = new(image.String(0xA151A8, 64), image.String(0xA15194, 64),
                Texture(0x32DC), Float(0x791D5D), Immediate(0x791D56),
                Texture(0x32E4), Float(0x791DA4), Immediate(0x791D9D)),
            DisplayName = "Four animated transformation sources; three particle variants, red halo and textured spark"
        };
    }
}


