using System.Numerics;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Native families whose particle vector has additional render passes or flare layers.</summary>
internal static class NativeLayeredEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var family = entry.NativeConstructorAddress switch
        {
            0x79BE50 => new NativeParticleFamily("cParticleSystem_naturehealing", entry.FactoryAddress,
                0x79BE50, 0x79BEF0, 0x79C1D0, 0x20A0 + SacredNatureHealingParticleStateLayout.SerializedSize + 8,
                0x20B8, 0x20D8, 1, 0x79BF60)
                { ColorOffset = 0x20A8, EmitBeforeMovement = false, SampleDefaultEnvironment = true,
                    DrawRegister = Register.EBP },
            0x79DC90 => new NativeParticleFamily("cParticleSystem_timecontrol", entry.FactoryAddress,
                0x79DC90, 0x79DD30, 0x79E030, 0x20A0 + SacredTimeControlParticleStateLayout.SerializedSize + 8,
                0x24A8, 0x24C8, 1, 0x79DE16)
                { ColorOffset = 0x20A8, EmitBeforeMovement = false,
                    // Event +38 is milliseconds, not a palette selector. Preview a three-second event.
                    EventArguments = new Dictionary<int, uint> { [0x38] = 3000 } },
            _ => null
        };
        if (family is null) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        family = family with { InitialVectorCount = capacity };
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var nature = family.Constructor == 0x79BE50;
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true,
            DisplayName = nature ? "Native healing: 8x8 atlas and additive glow" : "Native time control: three-second event",
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, Capacity = capacity,
            OneTime = true, EmitBeforeMovement = false, SingleBirthInitialization = true,
            RequiresActorContext = (memory.Integer(memory.BaseAddress + 0x7C, 4) & 0x1200) != 0,
            TextureBindings = bindings, ParameterSets = NativePresetReader.ReadParameters(memory, family),
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings),
            AdditionalDraws = nature ? [NativeTextureReader.ReadDraw(image, code,
                family with { DrawAddress = 0x79BFBF }, 0, bindings)] : [],
            AdditionalHalos = nature ? [] : ReadTimeControlHalos(image, code, bindings),
            EmissionDurationSeconds = nature ? image.Single((uint)code.At(0x79BF23).MemoryDisplacement64)
                : BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + 0x252C, 4)),
            EmissionClock = nature ? SacredParticleEmissionClock.CurrentTimeInclusive : SacredParticleEmissionClock.CrossingUpdate,
            RetireAfterDuration = !nature
        };
    }

    private static IReadOnlyList<SacredParticleHaloDefinition> ReadTimeControlHalos(
        SacredExecutableImage image, NativeCode code, IReadOnlyList<SacredParticleTextureBinding> bindings)
    {
        // The renderer's loop passes offset (0,0,2*i), size 40-1.75*i, SRC_ALPHA/ONE,
        // colour 0x60FF4040 and atlas side one to the native lensflare helper.
        var texture = bindings.Single(b => b.NativeHandleOffset == code.At(0x79DD9D).MemoryDisplacement64).TextureName;
        var count = checked((int)code.At(0x79DE0D).GetImmediate(1));
        var size = image.Single((uint)code.At(0x79DDFC).MemoryDisplacement64);
        var step = image.Single((uint)code.At(0x79DDF4).MemoryDisplacement64);
        var color = (uint)code.At(0x79DDEE).GetImmediate(0);
        return Array.AsReadOnly(Enumerable.Range(0, count).Select(i => new SacredParticleHaloDefinition(
            texture, (float)(size - i * (double)step), color, 0x79DE07)
            { Offset = new Vector3(0, 0, i * 2), SourceColorOnly = false }).ToArray());
    }
}
