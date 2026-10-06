// #pragma hlsl profile ps_6_6
#pragma vertex vs_main
#pragma fragment ps_sdr

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: painter depth, z: phase
}

cbuffer SceneConstants : register(b1)
{
    float4 light_direction_and_specular_strength;
    float4 camera_position_and_shininess;
    float4 ambient_color_and_intensity;
    float4 light_color_and_diffuse_intensity;
    float4 hdr_display; // x: scene paper white, y: surface-light influence, z: particle RGB gain, w: unlit RGB gain
    float scene_elapsed_seconds;
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
        if (texture_flags.x > 5.5f && texture_flags.x < 6.5f)
        {
            float cycle = frac(scene_elapsed_seconds * 1.8f + texture_flags.z);
            float pulse = saturate(sin(cycle * 3.14159265f));
            size_scale = lerp(0.3f, 1.1f, pulse);
        }
        world_position += (right * input.normal.x + up * input.normal.y) * model_scale * size_scale;
    }
    output.position = mul(float4(world_position, 1.0f), view_projection);
    if (texture_flags.y >= 0.0f)
    {
        float3 world_origin = mul(float4(0.0f, 0.0f, 0.0f, 1.0f), world).xyz;
        float3 local = world_position - world_origin;
        float local_depth = (local.y - local.z * 0.25f) / (24.0f * 1.41421356237f * 4096.0f);
        output.position.z = output.position.w * saturate(texture_flags.y + local_depth);
    }
    output.tex_coord = input.tex_coord;
    return output;
}

float2 animated_tex_coord(float2 tex_coord)
{
    if (texture_flags.x > 1.5f && texture_flags.x < 2.5f)
    {
        float frame = fmod(floor(scene_elapsed_seconds * 12.0f), 16.0f);
        float2 cell = float2(fmod(frame, 4.0f), floor(frame / 4.0f));
        return (tex_coord + cell) * 0.25f;
    }

    if (texture_flags.x > 7.5f && texture_flags.x < 8.5f)
    {
        float angle = scene_elapsed_seconds * 1.35f + texture_flags.z * 6.2831853f;
        float sine = sin(angle);
        float cosine = cos(angle);
        float2 centered = tex_coord - 0.5f;
        return float2(
            centered.x * cosine - centered.y * sine,
            centered.x * sine + centered.y * cosine) + 0.5f;
    }

    if (texture_flags.x > 3.5f && texture_flags.x < 4.5f)
        tex_coord.x += sin(tex_coord.y * 11.0f + scene_elapsed_seconds * 7.0f) * 0.08f;
    return tex_coord;
}

[earlydepthstencil]
float4 ps_sdr(vs_output input) : SV_Target
{
    float4 sampled = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord));
    // Native RGB lens flares use black as zero energy and carry no coverage
    // alpha. All existing SDR modes retain their authored alpha behavior.
    bool native_rgb_lens_flare = texture_flags.x > 10.5f && texture_flags.x < 11.5f;
    float alpha = (native_rgb_lens_flare ? 1.0f : sampled.a) * model_color.a * input.opacity;
    if (alpha < 0.02f)
        discard;

    float3 color = sampled.rgb * model_color.rgb;
    return float4(color, alpha);
}

// Texture.pak channel encoding selects the fragment shader; no item IDs are used.
[earlydepthstencil]
float4 ps_hdr_rgb(vs_output input) : SV_Target
{
    float3 sampled = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord)).rgb;
    float alpha = model_color.a * input.opacity;
    if (alpha < 0.02f) discard;
    return ComposeAdditiveParticle(sampled, model_color.rgb * hdr_display.z, alpha);
}

[earlydepthstencil]
float4 ps_hdr_argb(vs_output input) : SV_Target
{
    float4 sampled = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord));
    float alpha = sampled.a * model_color.a * input.opacity;
    if (alpha < 0.01f) discard;
    return ComposeAdditiveParticle(sampled.rgb, model_color.rgb * hdr_display.z, alpha);
}

[earlydepthstencil]
float4 ps_hdr_alpha_mask(vs_output input) : SV_Target
{
    float mask = particle_texture.Sample(particle_sampler, animated_tex_coord(input.tex_coord)).a;
    float alpha = mask * model_color.a * input.opacity;
    if (alpha < 0.01f) discard;
    return ComposeAdditiveParticle(1.0f.xxx, model_color.rgb * hdr_display.z, alpha);
}

[earlydepthstencil]
float4 ps_hdr(vs_output input) : SV_Target
{
    return ps_hdr_argb(input);
}
