using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Native line generators with a single standard render pass.</summary>
internal static class NativeLineEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var profile = entry.NativeConstructorAddress switch
        {
            0x77A5C0 => (Name: "cParticleSystem_magicfire", ConstructorEnd: 0x77A630u,
                Initializer: 0x77A8E0u, Render: 0x77A6C0u, Creation: 0x77A697u),
            0x77C0C0 => (Name: "cParticleSystem_magicgift", ConstructorEnd: 0x77C130u,
                Initializer: 0x77C3E0u, Render: 0x77C1C0u, Creation: 0x77C197u),
            _ => default
        };
        if (profile.Name is null) yield break;
        var call = code.At(profile.Creation);
        if (call.Mnemonic != Mnemonic.Call || call.NearBranch32 != 0x766200)
            throw new NotSupportedException("Unmapped native line generator.");
        var arguments = new List<Instruction>();
        for (var ip = profile.Creation - 0x10; ip < profile.Creation;)
        {
            var instruction = code.At(ip); ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Push) arguments.Add(instruction);
        }
        // These native updaters push mode 1, end attachment 1, then a tested zero release state.
        if (arguments.Count != 6 || arguments[0].GetImmediate(0) != 1 || arguments[1].GetImmediate(0) != 1)
            throw new NotSupportedException("Unmapped native line attachment arguments.");
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var family = new NativeParticleFamily(profile.Name, entry.FactoryAddress, entry.NativeConstructorAddress!.Value,
            profile.ConstructorEnd, profile.Initializer, 0x20A0 + SacredMagicLineParticleStateLayout.SerializedSize + 4,
            0x20BC, 0x20DC, 1, profile.Render)
            { ColorOffset = 0x20AC, EmitBeforeMovement = false, DrawRegister = Register.EBP,
                SelectorOffset = 0x3C, InitialVectorCount = capacity };
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true,
            DisplayName = "Native line emitter; strength input; preview attachment inputs",
            OneTime = false, EmitBeforeMovement = false, SingleBirthInitialization = true,
            RequiresActorContext = (memory.Integer(memory.BaseAddress + 0x7C, 4) & 0x1200) != 0,
            LineEmission = new(0, (int)arguments[1].GetImmediate(0), (int)arguments[0].GetImmediate(0), profile.Creation),
            Strength = NativeLineStrengthReader.Read(image, code, family.Initializer),
            Capacity = capacity, Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
            TextureBindings = bindings, ParameterSets = NativePresetReader.ReadParameters(memory, family),
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single())
        };
    }
}
