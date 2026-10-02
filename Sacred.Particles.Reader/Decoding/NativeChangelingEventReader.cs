using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Changeling's particle pass, independent of its gameplay transformation calls.</summary>
internal static class NativeChangelingEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.FactoryAddress != 0x5A1B93) yield break;
        foreach (var preset in new[] { 0, 1 })
        {
            var family = new NativeParticleFamily("cParticleSystem_changeling", entry.FactoryAddress,
                0x795B30, 0x795BA3, 0x795E20,
                0x20A0 + SacredChangelingParticleStateLayout.SerializedSize + 4, 0x20B8, 0x20D8, 1, 0x795C31)
            {
                ColorOffset = 0x20A8, DrawRegister = Register.EBP,
                // The actor/mesh calls occur between authored motion and color writes.
                // Stop before gameplay transformation and the stack epilogue.
                ActorLookup = (0x795EEC, 0x795F1B), InitializerEnd = 0x795FCB
            };
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            var bindings = NativeTextureReader.ReadBindings(image, code, family);
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
                ModelBurstCount = checked((int)code.At(0x795F01).GetImmediate(0)),
                GroundCollision = ParticleGroundCollision.Bounce,
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, TextureBindings = bindings,
                Draw = NativeTextureReader.ReadDraw(image, code, family, preset, bindings, bindings.Single()),
                ParameterSets = NativePresetReader.ReadParameters(memory, family)
            };
        }
    }
}
