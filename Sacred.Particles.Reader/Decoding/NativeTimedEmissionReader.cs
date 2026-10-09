using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Single-parameter emitters whose own update stops births after a native time limit.</summary>
internal static class NativeTimedEmissionReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var family = entry.NativeConstructorAddress switch
        {
            0x789E10 => new NativeParticleFamily("cParticleSystem_windstrike", entry.FactoryAddress,
                0x789E10, 0x789E73, 0x78A170, 0x2144, 0x20BC, 0x20DC, 1, 0x789F50)
                { ColorOffset = 0x20AC, EventColor = image.UInt32(0x761C56), DrawRegister = Register.EBX },
            0x775420 => new NativeParticleFamily("cParticleSystem_burningBone", entry.FactoryAddress,
                0x775420, 0x775490, 0x775750, 0x2144, 0x20BC, 0x20DC, 1, 0x775530)
                { ColorOffset = 0x20AC, EmitBeforeMovement = false, DrawRegister = Register.EBP },
            _ => null
        };
        if (family is null) yield break;
        var durationOperand = family.EmitBeforeMovement ? 0x789F0Fu : 0x775512u;
        var duration = image.Single((uint)code.At(durationOperand).MemoryDisplacement64);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            DisplayName = family.EmitBeforeMovement ? "Native RGB event (white preview)" : "Native event (zero time/size inputs)",
            EmitBeforeMovement = family.EmitBeforeMovement, EmissionDurationSeconds = duration,
            SingleBirthInitialization = true,
            RequiresActorContext = (memory.Integer(memory.BaseAddress + 0x7C, 4) & 0x1000) != 0,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
            TextureBindings = bindings,
            ParameterSets = NativePresetReader.ReadParameters(memory, family),
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single())
        };
    }
}
