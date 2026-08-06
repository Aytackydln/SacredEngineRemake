// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags;
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

struct vs_input { float3 position : POSITION; float3 normal : NORMAL; float2 tex_coord : TEXCOORD; };
struct vs_output { float4 position : SV_position; float2 tex_coord : TEXCOORD0; float opacity : TEXCOORD1; };

vs_output vs_main(vs_input input)
{
    vs_output output;
    float3 world_position = mul(float4(input.position, 1.0f), world).xyz;
    output.opacity = 1.0f;
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
        if (texture_flags.x > 5.5f && texture_flags.x < 6.5f)
        {
            float particle_phase = texture_flags.z + max(input.normal.z - 1.0f, 0.0f);
            float cycle = frac(texture_flags.w * 1.8f + particle_phase);
            float pulse = saturate(sin(cycle * 3.14159265f));
            output.opacity = smoothstep(0.08f, 0.42f, pulse);
            size_scale = lerp(0.3f, 1.1f, pulse);
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
        return (tex_coord + float2(fmod(frame, 4.0f), floor(frame / 4.0f))) * 0.25f;
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

float4 ps_main(vs_output input) : SV_Target
{
    float4 sampled = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord));
    bool uses_dense_composition = texture_flags.x > 4.5f && texture_flags.x < 7.5f;

    // Dense elemental fields use a soft mask authored in the 0..0.6 alpha range.
    // SDR builds its intensity by additively stacking many sprites. Reconstruct a
    // narrow, full-strength core from the authored range so source-over stacking
    // preserves that detail without broadening the whole soft mask.
    float dense_coverage = saturate(sampled.a / 0.6f);
    dense_coverage *= dense_coverage;
    float coverage = uses_dense_composition ? dense_coverage : sampled.a;
    float alpha = coverage * model_color.a * input.opacity;
    if (alpha < 0.02f)
        discard;

    // Alpha-authored streak textures carry their shape in alpha and are tinted by
    // model_color. Dense elemental textures carry both shape and color in RGB.
    float3 texture_color = uses_dense_composition ? sampled.rgb : 1.0f;
    float3 color = texture_color * model_color.rgb;
    // The render target is already PQ encoded, so its fixed-function blend unit
    // also operates on PQ values. Premultiply in that same domain to preserve
    // the authored coverage gradient during ordinary source-over composition.
    float3 hdr_color = SdrTextureToHdr10(color, hdr_display.w) * alpha;
    return float4(hdr_color, alpha);
}
