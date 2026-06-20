// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // y: 4x4 atlas when > 1.5, z: painter depth, w: elapsed seconds
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
        if (texture_flags.y > 5.5f && texture_flags.y < 6.5f)
        {
            float cycle = frac(texture_flags.w * 1.8f + texture_flags.z);
            output.opacity = saturate(sin(cycle * 3.14159265f));
            size_scale = lerp(0.45f, 1.1f, output.opacity);
        }
        world_position += (right * input.normal.x + up * input.normal.y) * model_scale * size_scale;
    }
    output.position = mul(float4(world_position, 1.0f), view_projection);
    output.tex_coord = input.tex_coord;
    return output;
}

float2 animated_tex_coord(float2 tex_coord)
{
    if (texture_flags.y > 1.5f && texture_flags.y < 2.5f)
    {
        float frame = fmod(floor(texture_flags.w * 12.0f), 16.0f);
        float2 cell = float2(fmod(frame, 4.0f), floor(frame / 4.0f));
        return (tex_coord + cell) * 0.25f;
    }

    if (texture_flags.y > 6.5f)
    {
        float angle = texture_flags.w * 1.6f + texture_flags.z * 6.2831853f;
        float sine = sin(angle);
        float cosine = cos(angle);
        float2 centered = tex_coord - 0.5f;
        return float2(centered.x * cosine - centered.y * sine,
                      centered.x * sine + centered.y * cosine) + 0.5f;
    }
    if (texture_flags.y > 3.5f && texture_flags.y < 4.5f)
        tex_coord.x += sin(tex_coord.y * 11.0f + texture_flags.w * 7.0f) * 0.08f;
    return tex_coord;
}

float4 ps_main(vs_output input) : SV_Target
{
    float4 sampled = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord));
    float brightness = max(max(sampled.r, sampled.g), sampled.b);
    bool uses_alpha = texture_flags.y > 2.5f && texture_flags.y < 4.5f;
    float coverage = uses_alpha ? sampled.a : smoothstep(0.02f, 0.92f, brightness) * sampled.a;
    float alpha = coverage * model_color.a * input.opacity;
    if (alpha < 0.02f)
        discard;

    float3 source_color = uses_alpha ? 1.0f : sampled.rgb / max(brightness, 0.08f);
    float intensity = uses_alpha ? 1.0f : saturate(brightness * 1.75f);
    float3 color = source_color * model_color.rgb * intensity;
    return float4(saturate(color), alpha);
}
