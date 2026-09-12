namespace Sacred.Core.World.Sector;

/// <summary>
/// Native weather event values used by Sacred's weather system. The names and
/// values come from <c>cEventWeather::__unnamed</c> in native metadata.
/// </summary>
public enum SectorWeatherKind : ushort
{
    None = 0,
    Reset = 1,
    Rain = 2,
    Fog = 3,
    GroundFog = 4,
    Snow = 5,
    Reserved = 6,
    Quake = 7,
    Night = 8,
    FlashOnce = 9,
    EnterSector = 10,
    ExitSector = 11,
    Stomp = 12,
}
