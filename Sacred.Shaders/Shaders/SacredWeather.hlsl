// Native Gold fog and snow textures, composed into the world before scaling and UI.
cbuffer WeatherConstants : register(b0)
{
    float2 viewport_size;
    float weather_time;
    float fog_reference_width;
    float3 fog_colour;
    float fog_strength;
    float3 rain_colour;
    float view_scale;
    float2 world_origin;
    float weather_mode; // 0: two fog layers; 1: rain; 2: snow
    float snow_strength;
    float3 snow_colour;
    float fog_reference_height;
    float4 fog_scroll;
};
Texture2D weather_texture : register(t0);
SamplerState weather_sampler : register(s0);

struct weather_vertex
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float hash(float2 value)
{
    float3 p = frac(float3(value.xyx) * .1031f);
    p += dot(p, p.yzx + 33.33f);
    return frac((p.x + p.y) * p.z);
}

weather_vertex vs_main(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    static const float2 corners[6] = { float2(0,0), float2(1,0), float2(0,1), float2(0,1), float2(1,0), float2(1,1) };
    weather_vertex output;
    float2 uv = corners[vertex];
    float2 position;
    if (weather_mode < .5f)
    {
        position = uv * viewport_size;
        // Gold 0x419498..0x4195A9: draw WEATHER_FOG01 twice with independent
        // scrolling UVs. The authored alpha plane defines the moving cloud shapes.
        output.uv = (position / view_scale + world_origin) / float2(fog_reference_width, fog_reference_height) +
            (instance == 0 ? fog_scroll.xy : fog_scroll.zw);
    }
    else if (weather_mode > 1.5f)
    {
        // Gold initializes 2..10 depth/size, falls by depth*10 per second and
        // adds a cosine sway (0x418820 / 0x41AABC), using PARTICLE_SPARK03.
        float depth = 2 + hash(float2(instance, 117)) * 8;
        float2 span = viewport_size / view_scale + 40;
        float y = hash(float2(instance, 73)) * span.y + weather_time * depth * 10;
        float x = hash(float2(instance, 31)) * span.x + weather_time * 6 + sin(y * .1f) * 5;
        position = (frac((float2(x,y) - world_origin) / span) * span - 20) * view_scale;
        position += (uv * 2 - 1) * depth * view_scale;
        output.uv = uv;
    }
    else
    {
        float seed = hash(float2(instance, 19));
        float speed = lerp(650, 1100, seed);
        float scale = max(.5f, viewport_size.y / 900.0f);
        float2 span = viewport_size + 100 * scale;
        position = frac(float2(hash(float2(instance, 31)), hash(float2(instance, 73))) +
            float2(-.24f, 1) * weather_time * speed * scale / span) * span - 50 * scale;
        float length = lerp(12, 27, seed) * scale;
        position += float2((uv.x - .5f) * 1.4f * scale - uv.y * length * .24f, uv.y * length);
        output.uv = uv;
    }
    output.position = float4(position / viewport_size * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

float4 ps_main(weather_vertex input) : SV_Target
{
    if (weather_mode < .5f)
    {
        float4 fog = weather_texture.Sample(weather_sampler, input.uv);
        float alpha = fog.a * fog_strength * (200.0f / 255.0f);
        return float4(fog.rgb * fog_colour * alpha, alpha);
    }
    if (weather_mode > 1.5f)
    {
        float4 flake = weather_texture.Sample(weather_sampler, input.uv);
        return float4(flake.rgb * snow_colour * flake.a, flake.a);
    }
    float opacity = .42f * (1 - abs(input.uv.x * 2 - 1)) * sin(input.uv.y * 3.14159265f);
    return float4(rain_colour * opacity, opacity);
}
