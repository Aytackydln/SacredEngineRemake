using Sacred.Core.World.Sector;

namespace Sacred.GameplayHost.World;

/// <summary>KEYX weather timers. Gold 0x625360 handles authored selectors 0 rain,
/// 1 fog and 2 snow. Other selectors must not be treated as weather event IDs.</summary>
public sealed class GameplaySectorWeather
{
    private readonly Random _random;
    private readonly double[] _remaining = new double[4];
    private readonly bool[] _active = new bool[4];
    private SectorCoord? _sector;
    public GameplaySectorWeather(int? seed = null) => _random = seed is { } value ? new(value) : new();

    public void Update(SectorCoord sector, SectorEnvironmentLayout? environment, float seconds, GameplayWeather weather)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_sector != sector)
        {
            _sector = sector;
            Array.Clear(_active);
            weather.SetTargets(0, 0, false);
            for (var i = 0; i < 4; i++)
                _remaining[i] = environment is { } initial ? Range(initial.Weather[i].Time0, initial.Weather[i].Time1) : double.PositiveInfinity;
        }
        if (environment is not { } data) return;
        for (var i = 0; i < 4; i++)
        {
            if (((byte)data.WeatherValid & (1 << i)) == 0) continue;
            var entry = data.Weather[i];
            var selector = entry.Type;
            if (selector is not (SectorWeatherKind.Rain or SectorWeatherKind.Fog or SectorWeatherKind.Snow)) continue;
            _remaining[i] -= seconds;
            if (_remaining[i] > 0) continue;
            _active[i] = !_active[i];
            var low = Math.Min(entry.Intensity0, entry.Intensity1);
            var high = Math.Max(entry.Intensity0, entry.Intensity1);
            var intensity = _active[i] ? _random.Next(low, high + 1) / 100f : 0;
            if (selector == SectorWeatherKind.Rain) weather.SetRain(intensity);
            else if (selector == SectorWeatherKind.Fog) weather.SetFog(intensity);
            else if (selector == SectorWeatherKind.Snow) weather.SetSnow(intensity);
            _remaining[i] = Math.Max(.01, _active[i] ? Range(entry.Duration0, entry.Duration1) : Range(entry.Time0, entry.Time1));
        }
    }
    public void Clear() => _sector = null;
    private double Range(uint first, uint last) => Math.Min(first, last) + _random.NextDouble() * ((double)Math.Max(first, last) - Math.Min(first, last));
}
