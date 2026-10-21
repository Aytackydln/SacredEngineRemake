using System.Numerics;

namespace Sacred.Engine.Scene;

/// <summary>Weather draw inputs in authored RGB; the final display pass owns HDR encoding.</summary>
public sealed class SceneWeather
{
    public float Rain { get; set; }
    public float Fog { get; set; }
    public float Snow { get; set; }
    public float Time { get; set; }
    public Vector3 FogColour { get; set; }
    public Vector3 RainColour { get; set; }
    public Vector3 SnowColour { get; set; }
    public Vector2 FogScroll0 { get; set; }
    public Vector2 FogScroll1 { get; set; }
}
