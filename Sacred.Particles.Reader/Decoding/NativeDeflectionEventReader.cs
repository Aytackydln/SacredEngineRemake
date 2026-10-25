namespace Sacred.Particles.Reader.Decoding;

internal static class NativeDeflectionEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredParticleDefinition entry)
    {
        if (entry.NativeConstructorAddress != 0x76C150) yield break;
        foreach (var selector in new[] { 0, 10 })
            yield return entry with
            {
                Preset = selector, IsEventPreset = true, OneTime = true,
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                NativeClass = "cParticleSystem_deflect", Deflection = SacredDeflectionFxDefinition.Original,
                SupportsAttachedEvent = true, NativeAttachmentFlags = 0x200,
                Draw = new(string.Empty, 4, 0x76C630), Capacity = 50
            };
    }
}
