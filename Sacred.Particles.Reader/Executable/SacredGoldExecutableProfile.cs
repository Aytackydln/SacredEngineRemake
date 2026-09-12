namespace Sacred.Particles.Reader.Executable;

/// <summary>Code/schema locations for the verified Sacred Gold build. These are
/// addresses, not item IDs or preset values; catalogue contents come from the executable.</summary>
internal static class SacredGoldExecutableProfile
{
    public const string CodeSha256 = "ee60108ce8147721717df1632c47b89c2b445a0255922219fa14a5980feadf37";
    public const uint ImageBase = 0x400000;
    public const uint CodeHashAddress = 0x401000;
    public const int CodeHashLength = 0x48EA32;
    public const uint LoaderEntryPoint = 0x1D6C3DB;
    public const uint LoaderHeaderAddress = 0x1D6D380;
    public const uint TypeTableAddress = 0x8EC328;
    public const int TypeTableCount = 0x15F8;
    public const uint FactoryDispatchAddress = 0x5A0C7A;
    public const uint CreationDispatchAddress = 0x48905B;
    public const uint ParticleQualityAddress = 0x182EE78;
    public const uint WorldUnitsPerTileAddress = 0x890DC0;
}
