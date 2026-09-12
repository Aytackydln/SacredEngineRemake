namespace Sacred.Core.World.Sector;

/// <summary>
/// Native cSectorEnvironment::eEVF at environment offset 0x00 (KEYX +0x1E9).
/// This is separate from the sector boundary/dungeon flags at KEYX +0x1CC.
/// </summary>
[Flags]
public enum SectorEnvironmentSections : uint
{
    None = 0,
    /// <summary>EVF_WEATHER.</summary>
    Weather = 1,
    /// <summary>EVF_ANIMALS.</summary>
    Animals = 2,
    /// <summary>EVF_MONSTER.</summary>
    Monsters = 4,
}
