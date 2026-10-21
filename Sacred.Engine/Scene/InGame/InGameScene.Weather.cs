using System;
using System.Globalization;
using Sacred.Core.World.Sector;
using Sacred.GameplayHost.World;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class InGameScene
{
    private readonly GameplayWeather _weather = new();
    private readonly GameplaySectorWeather _sectorWeather = new();
    private readonly GameplayWeatherEffects _weatherEffects = new();
    private bool _weatherPreview;
    private long _weatherNetworkRevision = -1;

    private void UpdateWeather(float seconds)
    {
        var position = _camera.WorldCenter;
        var coord = new SectorCoord((int)MathF.Floor(position.X / Sector.TileCount), (int)MathF.Floor(position.Y / Sector.TileCount));
        if (!_weatherPreview)
        {
            _sectorWeather.Update(coord, _worldArchive.GetEnvironment(coord), seconds, _weather);
            if (_networkPresentation is { } network)
            {
                var received = network.World.Replicas.Weather;
                _weather.Synchronize(received);
                if (_weatherNetworkRevision != received.Revision)
                {
                    _weatherNetworkRevision = received.Revision;
                    EngineLog.WriteLine($"Network weather synchronized: revision {received.Revision}; snapshot {received.SnapshotRevision}; commands {received.Recent.Count}.");
                }
            }
        }
        if (_networkPresentation is { } view) _weatherEffects.Synchronize(view.World.WorldObjects, _weather, EngineLog.WriteLine);
        _weather.Advance(seconds);
        ApplyWeather();
    }

    private void ApplyWeather()
    {
        var outdoors = _worldLighting.CurrentZone == WorldZone.Outdoors && _scene.Indoor.ActiveGroup is null;
        WorldWeatherLighting.Apply(_weather, _scene.Lighting, _scene.Weather, outdoors);
        _camera.SetShakeOffset(_weather.ShakeOffset);
    }

    private bool TrySetWeather(string option, string value, out string message)
    {
        message = string.Empty;
        if (option is not ("weather" or "rain" or "fog" or "snow" or "lightning" or "earthquake" or "shake" or "stomp")) return false;
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = $"weather {(_weatherPreview ? "preview" : "live")}: rain {_weather.Rain:F2}, fog {_weather.Fog:F2}, snow {_weather.Snow:F2}, storm {_weather.Storm}; lightning {_weather.Lightning:F2}; quake {_weather.QuakeStrength:F2}, stomp {_weather.StompStrength:F2}; camera {_weather.ShakeOffset}";
            return true;
        }
        if (option == "weather" && value.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            _weatherPreview = false; _weather.Clear(); _sectorWeather.Clear();
            message = "weather follows sector data and received network state"; return true;
        }
        if (option == "weather")
        {
            switch (value.ToLowerInvariant())
            {
                case "clear": case "off": _weather.Clear(); break;
                case "rain": _weather.SetTargets(.65f, 0, false); break;
                case "fog": _weather.SetTargets(0, 1, false); break;
                case "snow": _weather.SetTargets(0, 0, false, 1); break;
                case "storm": _weather.SetTargets(1, .25f, true); _weather.Flash(); break;
                default: message = "Usage: set weather <live|clear|rain|fog|snow|storm|status>"; return true;
            }
            _weatherPreview = true;
        }
        else if (option == "lightning")
        {
            if (value is not ("once" or "on")) { message = "Usage: set lightning once"; return true; }
            _weather.Flash();
        }
        else
        {
            var strength = value.ToLowerInvariant() switch { "on" => 1f, "off" => 0f, _ => float.NaN };
            if (float.IsNaN(strength) && (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out strength) ||
                    !float.IsFinite(strength) || strength is < 0 or > 1))
            { message = $"Usage: set {option} <0..1|on|off>"; return true; }
            if (option == "rain") { _weather.SetRain(strength); _weatherPreview = true; }
            else if (option == "fog") { _weather.SetFog(strength); _weatherPreview = true; }
            else if (option == "snow") { _weather.SetSnow(strength); _weatherPreview = true; }
            else _weather.Quake(strength, option == "stomp");
        }
        message = $"weather input: {option} {value}";
        return true;
    }
}
