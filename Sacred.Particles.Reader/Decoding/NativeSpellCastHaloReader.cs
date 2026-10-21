using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeSpellCastHaloReader
{
    public static IReadOnlyList<SacredParticleHaloDefinition> Read(SacredExecutableImage image, NativeCode code,
        int preset, float start, float end, IReadOnlyList<SacredParticleTextureBinding> bindings)
    {
        if (preset is 10 or >= 17 and <= 20)
        {
            var selector = preset == 10 ? 7U : 8U;
            var color = preset switch { 10 => 0xFF80FF80u, 17 => 0xFF14FF14u, 18 => 0xFFFF8014u, _ => 0xFF9090A0u };
            return [new(NativeTextureReader.ReadFlareTexture(code, selector), 0, color, preset == 10 ? 0x776FEBu : preset == 18 ? 0x776E14u : 0x776EB3u)
            {
                AttachmentIndex = -1, SourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, selector),
                VisibleFromSeconds = float.BitIncrement(start), VisibleUntilSeconds = float.BitDecrement(end),
                SizeCurve = new(start, end, image.Single(preset == 10 ? 0x8910B0u : 0x890948u)), PersistAfterEmission = true
            }];
        }
        if (preset != 22) return [];
        var texture = bindings.Single(binding => binding.NativeHandleOffset == 0x2550).TextureName;
        SacredParticleHaloDefinition Twirl(uint address, uint radius, uint rotation) => new(texture, 0, 0xFF804122, address)
        {
            GroundAligned = true, SourceColorOnly = true,
            VisibleFromSeconds = float.BitIncrement(start), VisibleUntilSeconds = float.BitDecrement(end),
            SizeCurve = new(start, end, image.Single(radius), SacredParticleHaloSizeCurveKind.QuadraticDecay),
            RotationRate = image.Single(rotation), PersistAfterEmission = true
        };
        return [Twirl(0x77718A, 0x89078C, 0x897C08), Twirl(0x7771C2, 0x8912BC, 0x897C04)];
    }
}
