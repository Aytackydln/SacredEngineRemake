// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: has texture, y: local depth scale, z: painter depth, w: alpha cutoff
}

cbuffer SceneConstants : register(b1)
{
    float4 light_position_and_specular_strength;
    float4 camera_position_and_shininess;
    float4 ambient_color_and_intensity;
    float4 light_color_and_diffuse_intensity;
    float4 hdr_display; // x: scene paper white, y: UI paper white, z: sun diffuse nits, w: sun specular nits
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
    float4 position : SV_position;
    float2 tex_coord : TEXCOORD0;
    float3 world_position : TEXCOORD1;
    float3 normal : TEXCOORD2;
};

float3 safe_normalize(float3 value, float3 fallback)
{
    float length_squared = dot(value, value);
    return length_squared > 0.000001f ? value * rsqrt(length_squared) : fallback;
}

vs_output vs_main(vs_input input)
{
    vs_output output;
    float4 world_position = mul(float4(input.position, 1.0f), world);
    float4 projected_position = mul(float4(input.position, 1.0f), world_view_projection);
    output.position = projected_position;
    if (texture_flags.y > 0.0f)
    {
        float4 projected_origin = mul(float4(0.0f, 0.0f, 0.0f, 1.0f), world_view_projection);
        float vertex_depth = projected_position.z / max(projected_position.w, 0.000001f);
        float origin_depth = projected_origin.z / max(projected_origin.w, 0.000001f);
        output.position.z = output.position.w * saturate(texture_flags.z + (vertex_depth - origin_depth) * texture_flags.y);
    }
    output.tex_coord = input.tex_coord;
    output.world_position = world_position.xyz;
    output.normal = safe_normalize(mul(float4(input.normal, 0.0f), world).xyz, float3(0.0f, 0.0f, 1.0f));
    return output;
}

float4 ps_main(vs_output input) : SV_Target
{
    float4 base_color = model_color;
    if (texture_flags.x > 0.5f)
        base_color = model_texture.Sample(model_sampler, input.tex_coord);

    if (base_color.a * model_color.a < texture_flags.w)
        discard;

    float3 normal = safe_normalize(input.normal, float3(0.0f, 0.0f, 1.0f));
    float3 light_position = light_position_and_specular_strength.xyz;
    float3 camera_position = camera_position_and_shininess.xyz;
    float3 light_vector = light_position - input.world_position;
    float3 view_vector = camera_position - input.world_position;
    float3 light_direction = safe_normalize(light_vector, float3(0.0f, -0.7071f, 0.7071f));
    float3 view_direction = safe_normalize(view_vector, float3(0.0f, -0.7071f, 0.7071f));

    float diffuse_amount = saturate(dot(normal, light_direction));
    float3 reflection_direction = reflect(-light_direction, normal);
    float specular_amount = diffuse_amount > 0.0f
        ? pow(saturate(dot(reflection_direction, view_direction)), max(camera_position_and_shininess.w, 1.0f))
        : 0.0f;

    float3 base_linear = SrgbToLinear(base_color.rgb);
    float3 ambient_nits =
        base_linear *
        ambient_color_and_intensity.rgb *
        (hdr_display.x * saturate(ambient_color_and_intensity.w));
    float3 sun_diffuse_nits =
        base_linear *
        light_color_and_diffuse_intensity.rgb *
        (hdr_display.z * diffuse_amount * max(light_color_and_diffuse_intensity.w, 0.0f));
    float3 sun_specular_nits =
        light_color_and_diffuse_intensity.rgb *
        (hdr_display.w * specular_amount * max(light_position_and_specular_strength.w, 0.0f));

    float3 hdr = Linear709NitsToHdr10(ambient_nits + sun_diffuse_nits + sun_specular_nits);
    return float4(hdr, base_color.a * model_color.a);
}
