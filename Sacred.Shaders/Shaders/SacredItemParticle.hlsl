// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_sdr

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: painter depth, z: phase, w: elapsed seconds
}

cbuffer SceneConstants : register(b1)
{
    float4 light_position_and_specular_strength;
    float4 camera_position_and_shininess;
    float4 ambient_color_and_intensity;
    float4 light_color_and_diffuse_intensity;
    float4 hdr_display;
}

Texture2D particle_texture : register(t0);
SamplerState particle_sampler : register(s0);

struct vs_input
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 tex_coord : TEXCOORD;
};

struct vs_output
{
    float4 position : SV_position;
    float2 tex_coord : TEXCOORD0;
    float opacity : TEXCOORD1;
};

vs_output vs_main(vs_input input)
{
    vs_output output;
    float3 world_position = mul(float4(input.position, 1.0f), world).xyz;
    output.opacity = 1.0f;
    bool is_short_lived_elemental_particle =
        texture_flags.x > 5.5f && texture_flags.x < 7.5f;
    if (input.normal.z > 0.5f)
    {
        float3 camera_direction = normalize(camera_position_and_shininess.xyz - world_position);
        float3 right = cross(camera_direction, float3(0.0f, 0.0f, 1.0f));
        if (dot(right, right) < 0.0001f)
            right = float3(1.0f, 0.0f, 0.0f);
        else
            right = normalize(right);
        float3 up = normalize(cross(right, camera_direction));
        float model_scale = length(mul(float4(1.0f, 0.0f, 0.0f, 0.0f), world).xyz);
        float size_scale = 1.0f;
        if (is_short_lived_elemental_particle)
        {
            float particle_phase = texture_flags.z + max(input.normal.z - 1.0f, 0.0f);
            // The weapon particles are emitted, rather than being a persistent glow
            // rigidly fixed to the weapon. A phase per quad keeps the short lives
            // staggered across the field.
            float cycle = frac(texture_flags.w * 4.0f + particle_phase);
            float pulse = saturate(sin(cycle * 3.14159265f));
            float angle = particle_phase * 6.2831853f;
            float3 drift = (right * cos(angle) + up * sin(angle)) * cycle * 0.9f;
            world_position += drift * model_scale;
            output.opacity = smoothstep(0.04f, 0.24f, pulse) *
                (1.0f - smoothstep(0.58f, 1.0f, cycle));
            size_scale = lerp(0.24f, 1.0f, pulse);
        }
        world_position += (right * input.normal.x + up * input.normal.y) * model_scale * size_scale;
    }
    output.position = mul(float4(world_position, 1.0f), view_projection);
    if (texture_flags.y >= 0.0f)
    {
        float painter_depth = texture_flags.y;
        if (texture_flags.x > 4.5f && texture_flags.x < 5.5f)
            painter_depth -= 0.00025f;
        float4 projected_origin = mul(mul(float4(0.0f, 0.0f, 0.0f, 1.0f), world), view_projection);
        float vertex_depth = output.position.z / max(output.position.w, 0.000001f);
        float origin_depth = projected_origin.z / max(projected_origin.w, 0.000001f);
        output.position.z = output.position.w * saturate(painter_depth + (vertex_depth - origin_depth) * 0.08f);
    }
    output.tex_coord = input.tex_coord;
    return output;
}

float2 animated_tex_coord(float2 tex_coord)
{
    if (texture_flags.x > 1.5f && texture_flags.x < 2.5f)
    {
        float frame = fmod(floor(texture_flags.w * 12.0f), 16.0f);
        float2 cell = float2(fmod(frame, 4.0f), floor(frame / 4.0f));
        return (tex_coord + cell) * 0.25f;
    }

    if (texture_flags.x > 7.5f && texture_flags.x < 8.5f)
    {
        float angle = texture_flags.w * 1.35f + texture_flags.z * 6.2831853f;
        float sine = sin(angle);
        float cosine = cos(angle);
        float2 centered = tex_coord - 0.5f;
        return float2(
            centered.x * cosine - centered.y * sine,
            centered.x * sine + centered.y * cosine) + 0.5f;
    }

    if (texture_flags.x > 3.5f && texture_flags.x < 4.5f)
        tex_coord.x += sin(tex_coord.y * 11.0f + texture_flags.w * 7.0f) * 0.08f;
    return tex_coord;
}

float4 ps_sdr(vs_output input) : SV_Target
{
    float4 sampled = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord));
    float coverage = 0.92f * sampled.a;
    float alpha = coverage * model_color.a * input.opacity;
    if (alpha < 0.02f)
        discard;

    // Modes 3/4 are alpha-mask textures. FX_STREAKS01 stores a useful alpha
    // channel with black RGB, so multiplying by sampled.rgb creates a black frame.
    bool uses_alpha_mask = texture_flags.x > 2.5f && texture_flags.x < 4.5f;
    float3 texture_color = uses_alpha_mask ? 1.0f : sampled.rgb;
    float3 color = texture_color * model_color.rgb;
    return float4(color * alpha, alpha);
}

float4 ps_hdr(vs_output input) : SV_Target
{
    float4 sampled = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord));
    bool uses_dense_composition = texture_flags.x > 4.5f && texture_flags.x < 7.5f;

    // Dense elemental fields use a soft mask authored in the 0..0.6 alpha range.
    // Reconstruct a narrow, full-strength core for source-over HDR composition.
    float dense_coverage = saturate(sampled.a / 0.6f);
    dense_coverage *= dense_coverage;
    float coverage = uses_dense_composition ? dense_coverage : sampled.a;
    float alpha = coverage * model_color.a * input.opacity;
    if (alpha < 0.02f)
        discard;

    float3 texture_color = uses_dense_composition ? sampled.rgb : 1.0f;
    float3 color = texture_color * model_color.rgb;
    // The HDR render target and its fixed-function blend unit both use PQ values.
    float3 hdr_color = SdrTextureToHdr10(color, hdr_display.w) * alpha;
    return float4(hdr_color, alpha);
}
