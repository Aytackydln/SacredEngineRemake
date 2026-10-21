namespace Sacred.Core.World.Sector;

/// <summary>
/// Authored KEYX selectors, distinct from cEventWeather IDs. Gold updateWeather
/// (0x625360) handles 0..2. Selector 4 is tested by the population ground-fog
/// predicate, but ignored by Gold's weather dispatcher. Other values stay unnamed.
/// </summary>
public enum SectorWeatherKind : ushort
{
    Rain = 0,
    Fog = 1,
    Snow = 2,
    GroundFog = 4,
}
