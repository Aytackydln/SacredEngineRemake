using System;
using System.Numerics;
using Sacred.GameplayHost.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Applies weather after a fresh day/night profile, before world drawing.</summary>
public static class WorldWeatherLighting
{
    public static void Apply(GameplayWeather weather, SceneLighting lighting, SceneWeather draw, bool outdoors)
    {
        var flash = outdoors ? weather.Lightning : 0;
        if (flash > 0)
        {
            // HDR retains energy above white. Modulate actual surface/texture colours
            // instead of covering the completed frame with a white rectangle.
            var boost = new Vector3(.85f, .92f, 1) * flash * 1.5f;
            lighting.WorldSurfaceAmbientColour += boost;
            lighting.AmbientColor = lighting.AmbientColor * lighting.AmbientIntensity + boost;
            lighting.AmbientIntensity = 1;
        }
        draw.Rain = outdoors ? weather.Rain : 0;
        draw.Fog = outdoors ? weather.Fog : 0;
        draw.Snow = outdoors ? weather.Snow : 0;
        draw.FogScroll0 = weather.FogScroll0;
        draw.FogScroll1 = weather.FogScroll1;
        draw.Time = weather.Time;
        var illumination = Vector3.Clamp(lighting.WorldSurfaceAmbientColour, Vector3.Zero, Vector3.One);
        // Gold 0x419447..0x419498 tints fog separately from terrain at night:
        // R/G = 255 - trunc(night*130/256), B = 255 - trunc(night*70/256).
        var night = Math.Clamp((int)MathF.Round(lighting.NightBlend * 255), 0, 255);
        var fogPalette = new Vector3(255 - (night * 130 >> 8), 255 - (night * 130 >> 8),
            255 - (night * 70 >> 8)) / 255f;
        // These are authored RGB values, like the world framebuffer. Darkening RGB
        // makes overlapping fog layers converge on gray. Native alpha controls
        // coverage; HDR presentation maps authored white to the frame's paper white.
        draw.FogColour = fogPalette;
        draw.RainColour = illumination * new Vector3(.45f, .52f, .60f);
        draw.SnowColour = illumination;
    }
}
