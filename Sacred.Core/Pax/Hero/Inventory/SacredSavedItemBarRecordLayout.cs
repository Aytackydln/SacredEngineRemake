namespace Sacred.Core.Pax.Hero.Inventory;

/// <summary>Gold taskbar hand record. Five interleaved pairs: offhand first, main hand second.</summary>
public static class SacredSavedItemBarRecordLayout
{
    public const int Size = 12;
    public const int ObjectReferenceOffset = 0;
    // Native runtime animation cache; saved reference remapping clears these words.
    public const int AnimationCacheWord1Offset = 4;
    public const int AnimationCacheWord2Offset = 8;
    public const int HandsPerSet = 2;
    public const int OffhandIndex = 0;
    public const int MainHandIndex = 1;
}
