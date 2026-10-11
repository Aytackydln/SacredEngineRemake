using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeHealingEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x773F30) yield break;
        var family = new NativeParticleFamily("cParticleSystem_healing", entry.FactoryAddress,
            0x773F30, 0x773FA0, 0x774330, 0x20E0, 0x20BC, 0, 1, 0x774152)
            { InitializerEnd = 0x774400 };
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(1));
        var burst = new SacredParticleRadialBurst(Operand(0x77405D), Operand(0x77404D), Operand(0x774053),
            Operand(0x7740A2), Operand(0x77409C), Operand(0x7740A8), Immediate(0x774082),
            Immediate(0x7740C5), Immediate(0x7740CC), 0x773FC0);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single());
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        for (var variant = 0; variant < 2; variant++)
        {
            var input = family with { InitialState = new Dictionary<int, uint> { [0xA9] = (uint)variant } };
            var memory = NativePresetReader.Evaluate(image, code, input, 0, quality, token);
            var motion = MemoryMarshal.Read<SacredParticleMotionLayout>(memory.ObjectBytes(0x20BC, 32));
            var colors = MemoryMarshal.Cast<byte, uint>(memory.ObjectBytes(0x20A8, 16)).ToArray();
            yield return entry with
            {
                NativeClass = family.Name, Preset = variant, IsEventPreset = true, OneTime = true,
                DisplayName = variant == 0 ? "Red radial healing burst" : "Blue radial healing burst",
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                TextureBindings = bindings, Draw = draw, Capacity = capacity, RadialBurst = burst,
                // Healing has no native stdCreation block. Only movement and corner colours are used.
                ParameterSets = Array.AsReadOnly<SacredParticleParameterSet>([new(0, default, motion) { Colors = Array.AsReadOnly(colors) }]),
                BurstOnFirstUpdate = true, EmitBeforeMovement = false
            };
        }
    }
}
