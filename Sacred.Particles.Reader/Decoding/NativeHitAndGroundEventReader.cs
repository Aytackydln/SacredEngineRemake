using System.Buffers.Binary;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Point-hit and ground decal emitters, identified by their native constructors.</summary>
internal static class NativeHitAndGroundEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress is not (0x7A58E0 or 0x78FFD0)) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        if (entry.NativeConstructorAddress == 0x7A58E0)
        {
            var family = new NativeParticleFamily("cParticleSystem_bloodpuddle", entry.FactoryAddress,
                0x7A58E0, 0x7A5951, 0x7A5C30,
                0x20A0 + SacredStandardEmitterParticleStateLayout.SerializedSize + 4,
                0x24A8, 0x24C8, 1, 0x7A5A28)
            {
                ColorOffset = 0x20A8, EmitBeforeMovement = false,
                // Explicit standalone sample inputs, not a recovered combat caller default.
                EventArguments = new Dictionary<int, uint> { [0x38] = 20, [0x3C] = 0x00800000 }
            };
            var duration = BinaryPrimitives.ReadDoubleLittleEndian(
                image.Slice((uint)code.At(0x7A5994).MemoryDisplacement64, 8));
            var decoded = Decode(image, code, entry, family, 0, quality, capacity, token);
            if (code.At(0x7A5A46).NearBranch32 != 0x653710)
                throw new NotSupportedException("Unmapped Blood Puddle draw matrix.");
            yield return decoded with
            {
                Draw = decoded.Draw! with { GroundAligned = true },
                SingleBirthInitialization = true, EmissionDurationSeconds = (float)duration,
                EmissionClock = SacredParticleEmissionClock.PreviousTimeExclusive,
                DisplayName = "Native ground emission; extent 20, RGB 800000"
            };
            yield break;
        }

        var special = checked((int)code.At(0x79039C).GetImmediate(1));
        foreach (var preset in new[] { 0, special })
        {
            var family = new NativeParticleFamily("cParticleSystem_sera_sm_hit", entry.FactoryAddress,
                0x78FFD0, 0x790041, 0x790340,
                0x20A0 + SacredSeraphimHitParticleStateLayout.SerializedSize + 4,
                0x24AC, 0x24CC, 1, 0x790110)
            {
                ColorOffset = 0x20AC, SelectorOffset = 0x3C, InitialVectorCount = capacity,
                // This special branch only schedules environment/audio work here.
                // The burst blocks and palette are evaluated on either side unchanged.
                ActorLookup = preset == special ? (0x7904F4, 0x790531) : null
            };
            yield return Decode(image, code, entry, family, preset, quality, capacity, token) with
            {
                BurstOnFirstUpdate = true,
                DisplayName = preset == special ? "Native special combat-art hit" : "Native standard combat-art hit"
            };
        }
    }

    private static SacredParticleDefinition Decode(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, NativeParticleFamily family, int preset,
        SacredParticleQuality quality, int capacity, CancellationToken token)
    {
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        return entry with
        {
            NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
            EmitBeforeMovement = family.EmitBeforeMovement, Capacity = capacity,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, TextureBindings = bindings,
            ParameterSets = NativePresetReader.Read(image, code, family, preset, quality, token),
            Draw = NativeTextureReader.ReadDraw(image, code, family, preset, bindings, bindings.Single())
        };
    }
}
