using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeBloodFountainEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x77BA00) yield break;
        var family = new NativeParticleFamily("Blood Fountain", entry.FactoryAddress, 0x77BA00, 0x77BA73,
            0x77BD60, 0x2534, 0x24AC, 0x24CC, 1, 0x77BB40)
        {
            ColorOffset = 0x20AC, SelectorOffset = 0x3C, EmitBeforeMovement = false,
            // Only randomized audio selection/pitch lies between these boundaries.
            ActorLookup = (0x77BFAA, 0x77C053)
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        foreach (var preset in new[] { 0, 1, 2 })
        {
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                BloodFountain = new()
                {
                    InitialState = MemoryMarshal.Read<SacredBloodFountainFxStateLayout>(memory.ObjectBytes(0x20A0, 0x490)),
                    FadeStart = Operand(0x77BAA6), FadeRate = Operand(0x77BAB7), JetSpeed = Operand(0x77BAE8)
                },
                Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
                EmitBeforeMovement = false, SingleBirthInitialization = true,
                GroundCollision = ParticleGroundCollision.Die,
                TextureBindings = bindings, ParameterSets = NativePresetReader.ReadParameters(memory, family),
                Draw = NativeTextureReader.ReadDraw(image, code, family, preset, bindings, bindings.Single()) with
                    { HalfSizeMultiplier = BitConverter.UInt32BitsToSingle(code.At(0x77BB75).Immediate32) },
                DisplayName = $"Native {(preset == 1 ? "green" : preset == 2 ? "dark" : "red")} fountain; five-second jet then fade"
            };
        }
    }
}
