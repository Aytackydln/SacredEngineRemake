using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Weather events emitted by native FX playback. These are resource-class rules,
/// independent of which hero or combat art created the object.</summary>
public sealed class GameplayWeatherEffects
{
    private readonly HashSet<(uint Reference, long Instance)> _played = [];
    private readonly HashSet<(uint Reference, long Instance)> _live = [];
    private readonly Dictionary<uint, (long Instance, long Position)> _positions = [];
    private readonly HashSet<uint> _references = [];

    public void Synchronize(IEnumerable<GoldReplicatedObject> objects, GameplayWeather weather,
        Action<string>? log = null)
    {
        _live.Clear();
        _references.Clear();
        foreach (var obj in objects)
        {
            var reference = obj.BaseState.ObjectReference;
            var key = (reference, obj.InstanceVersion);
            _live.Add(key);
            _references.Add(reference);
            if (obj.Creature is not null)
            {
                // Gold landings emit WEATHER_STOMP 18 (0x6395CC). A received
                // stop is consumed once; an initial snapshot never replays a landing.
                if (_positions.TryGetValue(reference, out var previous) && previous.Instance == obj.InstanceVersion &&
                    previous.Position != obj.PositionVersion && obj.PositionOpcode == GoldObjectFlightStopCodec.Opcode &&
                    (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0)
                {
                    weather.Quake(.18f, stomp: true);
                    log?.Invoke($"Weather stomp: landing ref {reference}; intensity 18.");
                }
                _positions[reference] = (obj.InstanceVersion, obj.PositionVersion);
            }
            if (obj.Fx is not { IsSystemActive: true } ||
                (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) continue;
            // Gold emits event 7 on the first advance of Ring of Poison and Kamikaze
            // (0x7AF5A3 / 0x7C17E3), Grenade Impact (0x7B70C3), and mine startup
            // (0x7BAE7A). Do not replay it on a position/state refresh of the same FX.
            var intensity = obj.BaseState.ObjectType switch
            {
                1008 or 1168 => 30,
                1020 => 24,
                1153 => 21,
                _ => 0
            };
            if (intensity == 0) continue;
            if (!_played.Add(key)) continue;
            weather.Quake(intensity / 100f);
            log?.Invoke($"Weather quake: FX {obj.BaseState.ObjectType}, ref {obj.BaseState.ObjectReference}; intensity {intensity}.");
        }
        // Retain consumed generations while their objects exist, including a stopped
        // system that can receive another state packet before its eventual removal.
        _played.RemoveWhere(key => !_live.Contains(key));
        foreach (var reference in _positions.Keys.Where(key => !_references.Contains(key)).ToArray()) _positions.Remove(reference);
    }
    public void Clear() { _played.Clear(); _live.Clear(); _positions.Clear(); _references.Clear(); }
}
