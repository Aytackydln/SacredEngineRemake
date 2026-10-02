using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Generic actor-mesh FX with complete standard or ground-bouncing movement parameters.</summary>
internal static class NativeGenericEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.FactoryAddress != 0x5A1BC9) yield break;
        // cCreatureHero::addExperience creates this family at 0x57F4FF, then
        // supplies style 2 at 0x57F54E and the default packed color at 0x57F523.
        foreach (var preset in new[] { 0, 2, 5, 9 })
        {
            var family = new NativeParticleFamily("cParticleSystem_generic", entry.FactoryAddress,
                0x796150, 0x796292, 0x796E10,
                0x20A0 + SacredGenericParticleStateLayout.SerializedSize + 0x3C, 0x24A8, 0x24C8, 1,
                preset == 9 ? 0x796B9Fu : 0x7969DFu)
            {
                SelectorOffset = 0x3C,
                // Generic colors are event input. Use the recovered level-up color or
                // the red scatter color authored by the actor-state caller at 0x541BDB.
                EventColor = preset switch { 2 => image.UInt32(0x57F524), 9 => 0, _ => image.UInt32(0x541BDC) },
                InitializerEnd = preset switch { 0 => 0x796ED8u, 2 => 0x797021u, 5 => 0x7971BDu, _ => 0x79764Eu },
                ActorLookup = preset == 9 ? (0x7975AAu, 0x7975DCu) : null
            };
            var bindings = NativeTextureReader.ReadBindings(image, code, family);
            var textureInstruction = code.At(image.UInt32(0x796C24 + (uint)preset * 4));
            var texture = bindings.Single(b => b.NativeHandleOffset == textureInstruction.MemoryDisplacement64);
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                DisplayName = preset switch { 0 => "Actor scatter", 2 => "Level-up", 5 => "Actor sparks", _ => "Actor impact smoke" },
                Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
                ModelBurstCount = checked((int)code.At(preset switch
                    { 0 => 0x796F24u, 2 => 0x797049u, 5 => 0x7971E5u, _ => 0x797653u }).GetImmediate(0)),
                // Update branches 0x79630F and 0x7965F2 call stdMovementWithGround
                // with the bounce argument zero; other decoded branches use stdMovement.
                GroundCollision = preset is 0 or 5 ? ParticleGroundCollision.Bounce : ParticleGroundCollision.None,
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, TextureBindings = bindings,
                Draw = NativeTextureReader.ReadDraw(image, code, family, preset, bindings, texture),
                ParameterSets = NativePresetReader.ReadParameters(memory, family)
            };
        }
    }
}
