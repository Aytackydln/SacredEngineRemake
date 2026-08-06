cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    // xy: horizontal sun slope, z: maximum world-space length, w: opacity
    float4 model_color;
    // x: texture mode, y: ground height, z: fixed painter depth
    float4 texture_flags;
}

Texture2D model_texture : register(t0);
SamplerState model_sampler : register(s0);

struct vs_input
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 tex_coord : TEXCOORD;
};

struct vs_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    float shadow_distance : TEXCOORD1;
};

vs_output vs_main(vs_input input)
{
    vs_output output;
    float3 world_position = mul(float4(input.position, 1.0f), world).xyz;
    float caster_height = max(world_position.z - texture_flags.y, 0.0f);
    float2 unbounded_offset = -model_color.xy * caster_height;
    float unbounded_distance = length(unbounded_offset);
    float shadow_distance = min(unbounded_distance, model_color.z);
    float offset_scale = unbounded_distance > 0.000001f
        ? shadow_distance / unbounded_distance
        : 0.0f;
    world_position.xy += unbounded_offset * offset_scale;
    world_position.z = texture_flags.y;

    output.position = mul(float4(world_position, 1.0f), world_view_projection);
    // Every caster uses exactly the same strict-Less depth. The first body/equipment
    // silhouette owns the pixel, so overlapping geometry cannot darken it repeatedly.
    output.position.z = output.position.w * texture_flags.z;
    output.tex_coord = input.tex_coord;
    output.shadow_distance = shadow_distance;
    return output;
}

float4 ps_main(vs_output input) : SV_Target
{
    float coverage = 1.0f;
    if (texture_flags.x > 0.5f)
    {
        coverage = model_texture.Sample(model_sampler, input.tex_coord).a;
        clip(coverage - 0.10f);
    }

    // Preserve a defined contact shadow, then smoothly diffuse the final part of
    // the projection. Vertices beyond the fixed limit collapse onto a fully
    // transparent boundary, so no shadow can extend past it.
    float normalized_distance = saturate(input.shadow_distance / max(model_color.z, 0.000001f));
    float end_fade = 1.0f - smoothstep(0.35f, 1.0f, normalized_distance);
    float alpha = model_color.a * end_fade * saturate(coverage * 2.0f);
    clip(alpha - (1.0f / 255.0f));
    return float4(0.0f, 0.0f, 0.0f, alpha);
}
