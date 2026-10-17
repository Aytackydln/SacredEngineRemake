namespace Sacred.Core.Pax.Hero;

/// <summary>Gold 0x6058A0 switch cases; compressed cases fall through to their raw import paths.</summary>
public static class SacredHeroPaxChunkTypes
{
    public const uint Hero = 0xC0;
    public const uint Inventory = 0xC1;
    public const uint Objects = 0xC2;
    public const uint Profile = 0xC3;
    public const uint StartInfo = 0xC4;
    public const uint SharedInventory = 0xC6;
    public const uint CompressedHero = 0xC7;
    public const uint CompressedObjects = 0xC8;
    public const uint CompressedInventory = 0xCA;
    public const uint CompressedSharedInventory = 0xCB;
    public const uint Scripts = 0xCC;
    public const uint CompressedScripts = 0xCD;
    public const uint Retained100 = 0xCE;
    /// <summary>Gold 0x606B3A copies chunk 0xCE into the cStats extension at +0x5724.</summary>
    public const uint StatisticsExtension = Retained100;

    public static uint GetDecodedType(uint type) => type switch
    {
        CompressedHero => Hero,
        CompressedObjects => Objects,
        CompressedInventory => Inventory,
        CompressedSharedInventory => SharedInventory,
        CompressedScripts => Scripts,
        _ => type
    };
}
