using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeHitFlashEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry)
    {
        if (entry.NativeConstructorAddress != 0x794570) yield break;
        var family = new NativeParticleFamily("Dark SM Hit", entry.FactoryAddress, 0x794570, 0x794681,
            0x794CE0, 0x2558, 0, 0, 0, 0x79491C);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        // The renderer indexes constructor handles directly with the low byte of event +44.
        var textures = bindings.Where(b => b.NativeHandleOffset >= 0x253C)
            .OrderBy(b => b.NativeHandleOffset).Select(b => b.TextureName).ToArray();
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0x416, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, TextureBindings = bindings,
            EventSelectorOffset = 0x3C, NativeAttachmentFlags = 0x40, RequiresActorContext = true,
            HitFlash = new(Array.AsReadOnly(textures),
                BitConverter.UInt32BitsToSingle((uint)code.At(0x794981).GetImmediate(0)),
                image.Single((uint)code.At(0x794708).MemoryDisplacement64),
                image.Single((uint)code.At(0x794958).MemoryDisplacement64))
            {
                Additive = code.At(0x794968).GetImmediate(0) != 0,
                SourceColorOnly = code.At(0x79496A).GetImmediate(0) == 0
            },
            DisplayName = "Received damage impact; event +44 selects the native hit texture"
        };
    }
}
