using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>All three event branches of the native Small Fire constructor.</summary>
internal static class NativeSmallFireEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x79F600) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var family = new NativeParticleFamily("cParticleSystem_smallfire", entry.FactoryAddress,
            0x79F600, 0x79F6D0, 0x79F970, 0x20A0 + SacredSmallFireParticleStateLayout.SerializedSize + 16,
            0x28B0, 0x28F0, 2, 0x79F906)
            { ColorOffset = 0x20B0, ColorSlotStride = SacredParticleColorTableLayout.SerializedSize,
                InitialVectorCount = capacity, EmitBeforeMovement = false };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        // Initializer subtracts zero then decrements twice before its default branch.
        for (var preset = 0; preset < 3; preset++)
        {
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            var drawAddress = preset switch { 0 => 0x79F906u, 1 => 0x79F8C2u, _ => 0x79F8EEu };
            var draw = NativeTextureReader.ReadDraw(image, code,
                family with { ParameterSlotCount = 1, DrawAddress = drawAddress }, preset, bindings, preset == 2 ? bindings.Single(b => b.NativeHandleOffset == code.At(0x79F8A5).MemoryDisplacement64) : null);
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                DisplayName = preset switch { 0 => "Native five-particle burst", 1 => "Native fire and smoke",
                    _ => "Native fire, smoke and animated ground-step flare" },
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, Capacity = capacity,
                EmitBeforeMovement = false, BurstOnFirstUpdate = preset == 0, SingleBirthInitialization = preset != 0,
                EmissionMode = preset == 0 ? checked((int)code.At(0x79F784).GetImmediate(0)) :
                    checked((int)code.At(0x79F71C).GetImmediate(0)),
                EmissionDurationSeconds = preset == 0 ? null : image.Single((uint)code.At(
                    preset == 1 ? 0x79F73Cu : 0x79F70Bu).MemoryDisplacement64),
                EmissionClock = SacredParticleEmissionClock.PreviousTimeExclusive,
                TextureBindings = bindings, Draw = draw, ParameterSets = NativePresetReader.ReadParameters(memory, family),
                ParticlesVisible = preset != 2 || quality >= SacredParticleQuality.High,
                AdditionalHalos = preset == 2 ? [ReadFlare(image, code, bindings)] : []
            };
        }
    }

    private static SacredParticleHaloDefinition ReadFlare(SacredExecutableImage image, NativeCode code,
        IReadOnlyList<SacredParticleTextureBinding> bindings)
    {
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        return new(bindings.Single(b => b.NativeHandleOffset == code.At(0x79F7F5).MemoryDisplacement64).TextureName,
            BitConverter.UInt32BitsToSingle((uint)code.At(0x79F88A).GetImmediate(0)),
            (uint)code.At(0x79F888).GetImmediate(0), 0x79F893)
        {
            Additive = code.At(0x79F87F).GetImmediate(0) != 0,
            SourceColorOnly = code.At(0x79F87D).GetImmediate(0) == 0,
            PersistAfterEmission = true,
            AtlasSide = checked((int)code.At(0x79F82E).GetImmediate(0)),
            Rotation = BitConverter.UInt32BitsToSingle((uint)code.At(0x79F883).GetImmediate(0)),
            AtlasAnimation = new(Operand(0x79F80F), Operand(0x79F838), Operand(0x79F851),
                Operand(0x79F864), Operand(0x79F86C), checked((int)code.At(0x79F877).GetImmediate(1)))
        };
    }
}
