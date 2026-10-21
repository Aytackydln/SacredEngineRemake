using System.Numerics;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Frame-rate-independent local weather playback. Never changes received state.</summary>
public sealed class GameplayWeather
{
    private readonly Random _random;
    private float _rainTarget, _fogTarget, _snowTarget;
    private float _quake, _stomp;
    private float _nextFlash = 20;
    private float _flashAge = 1;
    private float _shakeClock;
    private long _received = -1;
    public GameplayWeather(int? seed = null) => _random = seed is { } value ? new(value) : new();
    public float Rain { get; private set; }
    public float Fog { get; private set; }
    public float Snow { get; private set; }
    /// <summary>Gold 0x41A8A1..0x41A983: two independently scrolling fog texture layers.</summary>
    public Vector2 FogScroll0 { get; private set; }
    public Vector2 FogScroll1 { get; private set; }
    public bool Storm { get; private set; }
    public float Lightning => _flashAge < .32f
        ? MathF.Exp(-_flashAge * 22) + .45f * MathF.Exp(-MathF.Abs(_flashAge - .12f) * 65) : 0;
    public Vector2 ShakeOffset { get; private set; }
    public float Time { get; private set; }
    public float QuakeStrength => _quake;
    public float StompStrength => _stomp;

    public void Synchronize(GoldWeatherReplica replica)
    {
        if (_received == replica.Revision) return;
        if (replica.SnapshotRevision > _received)
        {
            if (replica.Current is { } state) LoadSnapshot(state);
            else Clear();
        }
        foreach (var notification in replica.Recent)
            if (notification.Version > Math.Max(_received, replica.SnapshotRevision)) Apply(notification.Event);
        _received = replica.Revision;
    }

    private void LoadSnapshot(GoldWeatherState state)
    {
        SetTargets((state.Flags & GoldWeatherLayout.RainFlag) != 0 ? state.RainParticles / 3000f : 0,
            (state.Flags & GoldWeatherLayout.FogFlag) != 0 ? state.FogOpacity / 200f : 0,
            (state.Flags & GoldWeatherLayout.LightningFlag) != 0,
            (state.Flags & GoldWeatherLayout.SnowFlag) != 0 ? state.SnowParticles / 3000f : 0);
        _quake = (state.Flags & GoldWeatherLayout.QuakeFlag) != 0 && float.IsFinite(state.QuakeStrength)
            ? Math.Clamp(state.QuakeStrength, 0, 100f / 3) : 0;
    }

    public void Apply(GoldWeatherEvent weather)
    {
        var strength = Math.Min(weather.Intensity, 100) / 100f;
        switch (weather.Kind)
        {
            case GoldWeatherEventKind.None: Clear(); break;
            case GoldWeatherEventKind.Reset: SetTargets(0, 0, false); Quake(0); break;
            case GoldWeatherEventKind.Rain: SetRain(strength); break;
            case GoldWeatherEventKind.Fog: SetFog(strength); break;
            case GoldWeatherEventKind.Snow: SetSnow(strength); break;
            case GoldWeatherEventKind.Quake: Quake(strength); break;
            case GoldWeatherEventKind.Stomp: Quake(strength, stomp: true); break;
            case GoldWeatherEventKind.FlashOnce: if (weather.Intensity == 0) _flashAge = 1; else Flash(); break;
            // Gold ignores GroundFog/Reserved. Other commands remain available in the replica.
        }
    }

    public void SetTargets(float rain, float fog, bool storm, float snow = 0)
    {
        _rainTarget = Unit(rain); _fogTarget = Unit(fog); _snowTarget = Unit(snow); Storm = storm;
    }
    public void SetRain(float strength) { _rainTarget = Unit(strength); Storm = _rainTarget > .75f; }
    public void SetFog(float strength) => _fogTarget = Unit(strength);
    public void SetSnow(float strength) => _snowTarget = Unit(strength);
    public void Flash() => _flashAge = 0;
    /// <summary>Gold weather events 7/12: intensity / 3, decaying by 10/40 units per second.</summary>
    public void Quake(float intensity, bool stomp = false)
    {
        var strength = Unit(intensity) * (100f / 3);
        if (stomp) _stomp = strength == 0 ? 0 : MathF.Max(_stomp, strength);
        else _quake = strength == 0 ? 0 : MathF.Max(_quake, strength);
    }

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Time = (Time + seconds) % 4096;
        Rain = Approach(Rain, _rainTarget, seconds * .6f);
        Fog = Approach(Fog, _fogTarget, seconds * .3f);
        Snow = Approach(Snow, _snowTarget, seconds * 1.2f);
        if (Fog > 0)
        {
            FogScroll0 = Wrap(FogScroll0 + new Vector2(.045f, .028f) * seconds);
            FogScroll1 = Wrap(FogScroll1 + new Vector2(.079f, .061f) * seconds);
        }
        _flashAge += seconds;
        if (Storm)
        {
            _nextFlash -= seconds;
            if (_nextFlash <= 0) { Flash(); _nextFlash = 20 * (1 + _random.NextSingle()); }
        }
        else _nextFlash = 20;
        _quake = MathF.Max(0, _quake - 10 * seconds);
        _stomp = MathF.Max(0, _stomp - 40 * seconds);
        _shakeClock = (_shakeClock + seconds) % 100;
        var strength = MathF.Max(_quake, _stomp);
        ShakeOffset = strength <= 0 ? Vector2.Zero : new Vector2(
            MathF.Sin(_shakeClock * 107) * .7f + MathF.Sin(_shakeClock * 173) * .3f,
            MathF.Sin(_shakeClock * 131 + 1) * .7f + MathF.Sin(_shakeClock * 191) * .3f) * strength;
    }
    public void Clear()
    {
        _rainTarget = _fogTarget = _snowTarget = Rain = Fog = Snow = _quake = _stomp = 0;
        Storm = false; _flashAge = 1; _nextFlash = 20; _received = -1;
        ShakeOffset = Vector2.Zero;
    }
    private static Vector2 Wrap(Vector2 value) => value - new Vector2(MathF.Floor(value.X), MathF.Floor(value.Y));
    private static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static float Approach(float value, float target, float step) => value < target
        ? MathF.Min(value + step, target) : MathF.Max(value - step, target);
}
