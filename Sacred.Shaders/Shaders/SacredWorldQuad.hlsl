// #pragma hlsl profile ps_5_0
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
    float jitter_x;
    float2 motion_vector;
    float history_weight;
    float jitter_y;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
};

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
// spatial reconstruction, 3 FSR 2-style temporal reconstruction.
float4 sample_point(float2 uv)
{
    uint width, height;
    texture0.GetDimensions(width, height);
    uint2 pixel = min(uint2(uv * float2(width, height)), uint2(width - 1, height - 1));
    return texture0.Load(int3(pixel, 0));
}

float4 sample_fsr1(float2 uv)
{
    uint width, height;
    texture0.GetDimensions(width, height);
    float2 texel = 1.0f / float2(width, height);
    float4 center = texture0.Sample(sampler0, uv);
    float4 cross = texture0.Sample(sampler0, uv + float2(texel.x, 0)) +
        texture0.Sample(sampler0, uv - float2(texel.x, 0)) +
        texture0.Sample(sampler0, uv + float2(0, texel.y)) +
        texture0.Sample(sampler0, uv - float2(0, texel.y));
    // Lightweight RCAS-style sharpening after bilinear reconstruction. It is spatial-only,
    // which is the FSR 1 property that lets this pass avoid velocity buffers entirely.
    return center * 1.35f - cross * 0.0875f;
}

float4 sample_upscaled(float2 uv)
{
    if (ambient_colour.x < 0.5f)
        return sample_point(uv);
    if (ambient_colour.x < 1.5f)
        return texture0.Sample(sampler0, uv);
    if (ambient_colour.x < 2.5f)
        return sample_fsr1(uv);

    uint width, height;
    texture0.GetDimensions(width, height);
    float2 texel = 1.0f / float2(width, height);
    float2 jittered_uv = uv + float2(jitter_x, jitter_y) * texel;
    float4 current = texture0.Sample(sampler0, jittered_uv);

    float4 north = texture0.Sample(sampler0, jittered_uv - float2(0.0f, texel.y));
    float4 south = texture0.Sample(sampler0, jittered_uv + float2(0.0f, texel.y));
    float4 west = texture0.Sample(sampler0, jittered_uv - float2(texel.x, 0.0f));
    float4 east = texture0.Sample(sampler0, jittered_uv + float2(texel.x, 0.0f));
    float4 neighborhood_min = min(current, min(min(north, south), min(west, east)));
    float4 neighborhood_max = max(current, max(max(north, south), max(west, east)));

    float2 history_uv = uv + motion_vector / viewport_size;
    bool history_in_bounds = all(history_uv >= 0.0f) && all(history_uv <= 1.0f);
    float4 history = texture1.Sample(sampler0, history_uv);
    history = clamp(history, neighborhood_min, neighborhood_max);

    float current_luma = dot(current.rgb, float3(0.2126f, 0.7152f, 0.0722f));
    float history_luma = dot(history.rgb, float3(0.2126f, 0.7152f, 0.0722f));
    float relative_difference = abs(current_luma - history_luma) /
        max(max(current_luma, history_luma), 0.05f);
    float temporal_weight = history_in_bounds
        ? history_weight * saturate(1.0f - relative_difference * 1.5f)
        : 0.0f;
    float4 temporal = lerp(current, history, temporal_weight);

    // A restrained RCAS-like finish restores edges softened by temporal accumulation.
    float4 cross_average = (north + south + west + east) * 0.25f;
    float4 sharpened = temporal + (current - cross_average) * 0.12f;
    return float4(max(sharpened.rgb, 0.0f), saturate(sharpened.a));
}

float4 ps_sdr_upscale(vertex_output input) : SV_Target
{
    return sample_upscaled(input.tex_coord);
}

float4 ps_hdr_upscale(vertex_output input) : SV_Target
{
    return sample_upscaled(input.tex_coord);
}
