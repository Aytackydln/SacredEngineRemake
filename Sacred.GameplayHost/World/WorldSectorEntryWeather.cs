using Sacred.Core.World.Sector;

namespace Sacred.GameplayHost.World;

/// <summary>The world effects of cEventWeather ENTERSECTOR (kind 10),
/// dispatched directly by 62625B to 419A70. It is not an Engine/UI event.
/// Ordinary rain/fog/snow settings cause no entry effect or network forwarding.
/// Selector 5's actual positioned factory remains a separate dependency.</summary>
public static class WorldSectorEntryWeather
{
    public static bool RequiresCreatureCreation(SectorEnvironmentLayout environment)
    {
        if ((byte)environment.WeatherValid == 0) return false;
        for (var index = 0; index < 4; index++)
            if (environment.Weather[index].Type == SectorWeatherKind.SectorEntryCreatures) return true;
        return false;
    }

    public static void Enter(SectorEnvironmentLayout environment)
    {
        if (RequiresCreatureCreation(environment))
            throw new NotSupportedException("Sector-entry weather selector 5 requires its actual positioned creature factory before notification completes.");
        // Original 41A187 excludes kind 10 from multiplayer forwarding. With
        // no selector-5 body, the complete handler has no persistent mutation.
    }
}
