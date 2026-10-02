// #pragma hlsl profile ps_6_6
#pragma vertex vs_main
#pragma fragment ps_sdr

Texture2D texture0 : register(t0);
Texture2D texture1 : register(t1);
SamplerState sampler0 : register(s0);

cbuffer QuadConstants : register(b0)
{
    float4 rect;
    float2 viewport_size;
    float premultiplied_alpha;
    float paper_white_nits;
    float3 ambient_colour;
    float history_uv_scale;
    float2 camera_motion_pixels;
    float history_weight;
    float temporal_padding;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
};

static const float fsr1_stationary_sharpening = 0.35f;
static const float fsr1_moving_sharpening = 0.10f;
static const float fsr1_full_motion_pixels = 12.0f;
static const float temporal_reconstruction_smoothing = 0.12f;

float surface_lighting(float2 pixel_position)
{
    return texture1.Sample(sampler0, pixel_position / viewport_size).r;
}

static const float2 quad_uvs[6] =
{
    float2(0.0f, 0.0f),
    float2(1.0f, 0.0f),
    float2(0.0f, 1.0f),
    float2(0.0f, 1.0f),
    float2(1.0f, 0.0f),
    float2(1.0f, 1.0f)
};

vertex_output vs_main(uint vertex_id : SV_VertexID)
{
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = rect.xy + uv * rect.zw;
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f
    );

    vertex_output output;
    output.position = float4(clip, 0.0f, 1.0f);
    output.tex_coord = uv;
    return output;
}

float4 ps_sdr(vertex_output input) : SV_Target
{
    float4 color = texture0.Sample(sampler0, input.tex_coord);
    color.rgb *= ambient_colour + surface_lighting(input.position.xy);
    return color;
}

float4 ps_sdr_screen(vertex_output input) : SV_Target
{
    return texture0.Sample(sampler0, input.tex_coord);
}

float4 ps_hdr(vertex_output input) : SV_Target
{
    float4 tex = texture0.Sample(sampler0, input.tex_coord);
    tex.rgb *= ambient_colour + surface_lighting(input.position.xy);

    return float4(SdrTextureToHdr10(tex.rgb, paper_white_nits), tex.a);
}

float4 ps_hdr_screen(vertex_output input) : SV_Target
{
    float4 tex = texture0.Sample(sampler0, input.tex_coord);

    return float4(SdrTextureToHdr10(tex.rgb, paper_white_nits), tex.a);
}

// ambient_colour.x selects the presentation filter: 0 point, 1 bilinear, 2 FSR 1-style
// spatial reconstruction, 3 FSR 2-style temporal reconstruction,
// 4 spatial reconstruction with motion-adaptive sharpening, 5 Lanczos2.
// The engine resolves mode 6 (FSR 2 / Lanczos2) from the current resolution ratio.
float4 sample_point(float2 uv)
{
    uint width, height;
    texture0.GetDimensions(width, height);
    uint2 pixel = min(uint2(uv * float2(width, height)), uint2(width - 1, height - 1));
    return texture0.Load(int3(pixel, 0));
}

float4 sample_fsr1(float2 uv, float sharpening_strength)
{
    uint width, height;
    texture0.GetDimensions(width, height);
    float2 texel = 1.0f / float2(width, height);
    float4 center = texture0.Sample(sampler0, uv);
    float4 neighbor_average = (texture0.Sample(sampler0, uv + float2(texel.x, 0)) +
        texture0.Sample(sampler0, uv - float2(texel.x, 0)) +
        texture0.Sample(sampler0, uv + float2(0, texel.y)) +
        texture0.Sample(sampler0, uv - float2(0, texel.y))) * 0.25f;
    // Lightweight RCAS-style sharpening after bilinear reconstruction. It is spatial-only,
    // which is the FSR 1 property that lets this pass avoid velocity buffers entirely.
    return center + (center - neighbor_average) * sharpening_strength;
}

float2 camera_velocity_pixels(float2 uv)
{
    float2 zoom_velocity_pixels =
        (uv - 0.5f) * viewport_size * (history_uv_scale - 1.0f);
    return camera_motion_pixels + zoom_velocity_pixels;
}

float motion_adaptive_fsr1_sharpening(float2 uv)
{
    float motion_amount = saturate(length(camera_velocity_pixels(uv)) / fsr1_full_motion_pixels);
    return lerp(fsr1_stationary_sharpening, fsr1_moving_sharpening, motion_amount);
}

float lanczos2_weight(float distance)
{
    float x = abs(distance);
    if (x < 0.0001f)
        return 1.0f;
    if (x >= 2.0f)
        return 0.0f;
    float angle = 3.14159265359f * x;
    return sin(angle) * sin(angle * 0.5f) / (angle * angle * 0.5f);
}

float4 sample_lanczos2(float2 uv)
{
    uint width, height;
    texture0.GetDimensions(width, height);
    float2 source_size = float2(width, height);
    // Widen the two-lobe kernel in source pixels when reducing resolution.
    // A fixed 4x4 footprint would alias whenever multiple source pixels collapse.
    float2 scale = max(source_size / viewport_size, 1.0f);
    float2 center = uv * source_size - 0.5f;
    int2 first = int2(ceil(center - 2.0f * scale));
    int2 last = int2(floor(center + 2.0f * scale));
    float4 color = 0.0f;
    float weight_sum = 0.0f;
    [loop]
    for (int y = first.y; y <= last.y; ++y)
    {
        float wy = lanczos2_weight((y - center.y) / scale.y);
        [loop]
        for (int x = first.x; x <= last.x; ++x)
        {
            float weight = wy * lanczos2_weight((x - center.x) / scale.x);
            int2 pixel = clamp(int2(x, y), int2(0, 0), int2(width - 1, height - 1));
            color += texture0.Load(int3(pixel, 0)) * weight;
            weight_sum += weight;
        }
    }
    color /= max(weight_sum, 0.0001f);
    // Preserve HDR values above one while removing negative ringing.
    return float4(max(color.rgb, 0.0f), saturate(color.a));
}

float4 sample_upscaled(float2 uv)
{
    if (ambient_colour.x >= 4.5f)
        return sample_lanczos2(uv);
    if (ambient_colour.x < 0.5f)
        return sample_point(uv);
    if (ambient_colour.x < 1.5f)
        return texture0.Sample(sampler0, uv);
    if (ambient_colour.x < 2.5f)
        return sample_fsr1(uv, fsr1_stationary_sharpening);
    if (ambient_colour.x >= 3.5f)
        return sample_fsr1(uv, motion_adaptive_fsr1_sharpening(uv));

    uint width, height;
    texture0.GetDimensions(width, height);
    float2 texel = 1.0f / float2(width, height);
    float4 current = texture0.Sample(sampler0, uv);

    float4 north = texture0.Sample(sampler0, uv - float2(0.0f, texel.y));
    float4 south = texture0.Sample(sampler0, uv + float2(0.0f, texel.y));
    float4 west = texture0.Sample(sampler0, uv - float2(texel.x, 0.0f));
    float4 east = texture0.Sample(sampler0, uv + float2(texel.x, 0.0f));
    float4 neighborhood_min = min(current, min(min(north, south), min(west, east)));
    float4 neighborhood_max = max(current, max(max(north, south), max(west, east)));
    float4 neighbor_average = (north + south + west + east) * 0.25f;
    float4 reconstruction = lerp(current, neighbor_average, temporal_reconstruction_smoothing);

    float2 history_uv = uv + camera_velocity_pixels(uv) / viewport_size;
    bool history_in_bounds = all(history_uv >= 0.0f) && all(history_uv <= 1.0f);
    float4 history = texture1.Sample(sampler0, history_uv);
    history = clamp(history, neighborhood_min, neighborhood_max);

    float current_luma = dot(reconstruction.rgb, float3(0.2126f, 0.7152f, 0.0722f));
    float history_luma = dot(history.rgb, float3(0.2126f, 0.7152f, 0.0722f));
    float relative_difference = abs(current_luma - history_luma) /
        max(max(current_luma, history_luma), 0.05f);
    float temporal_weight = history_in_bounds
        ? history_weight * saturate(1.0f - relative_difference * 1.5f)
        : 0.0f;
    float4 temporal = lerp(reconstruction, history, temporal_weight);
    // This value is captured as next frame's history. Sharpening here would feed back
    // recursively; it belongs in a separate presentation pass after history capture.
    return float4(max(temporal.rgb, 0.0f), saturate(temporal.a));
}

float4 ps_shadow(vertex_output input) : SV_Target
{
    float opacity = min(texture0.Sample(sampler0, input.tex_coord).r, 0.85f);
    return float4(0.0f, 0.0f, 0.0f, opacity);
}

float4 ps_sdr_upscale(vertex_output input) : SV_Target
{
    return sample_upscaled(input.tex_coord);
}

float4 ps_hdr_upscale(vertex_output input) : SV_Target
{
    return sample_upscaled(input.tex_coord);
}
