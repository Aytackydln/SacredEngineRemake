namespace Sacred.Core.Pak.Texture;

/// <summary>
/// Texture archive handle selected by Gold's in-memory texture entry. Gold
/// probes Texture.pak followed by Texture00.pak through Texture15.pak.
/// </summary>
public enum SacredTexturePakArchiveSlot : byte
{
    Texture = 0,
    Texture00 = 1,
    Texture01 = 2,
    Texture02 = 3,
    Texture03 = 4,
    Texture04 = 5,
    Texture05 = 6,
    Texture06 = 7,
    Texture07 = 8,
    Texture08 = 9,
    Texture09 = 10,
    Texture10 = 11,
    Texture11 = 12,
    Texture12 = 13,
    Texture13 = 14,
    Texture14 = 15,
    Texture15 = 16
}
