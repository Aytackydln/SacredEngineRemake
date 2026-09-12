namespace Sacred.Particles.Generated;

internal static partial class EmbeddedParticleCatalogueMedium
{
    internal static SacredParticleCatalogue Create()
    {
        SacredParticleCatalogue? catalogue = null;
        Populate(ref catalogue);
        return catalogue ?? throw new InvalidOperationException("Regenerate the embedded particle catalogue using Sacred.Particles.Reader.");
    }
    static partial void Populate(ref SacredParticleCatalogue? catalogue);
}
