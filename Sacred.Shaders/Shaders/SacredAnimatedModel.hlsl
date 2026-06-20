// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: signed packed animation, z: painter depth, w: scaled animation time
}

cbuffer SceneConstants : register(b1)
{
    float4 light_position_and_specular_strength;
    float4 camera_position_and_shininess;
    float4 ambient_color_and_intensity;
    float4 light_color_and_diffuse_intensity;
}

Texture2D model_texture : register(t0);
Texture2D model_overlay_texture : register(t1);
SamplerState model_sampler : register(s0);

static const float texture_animation_scroll_mode_threshold = 0.25f;
static const float texture_animation_clamp_mode_threshold = 0.60f;
static const float texture_animation_black_key_threshold = 0.03f;
static const float texture_animation_black_key_scale = 12.0f;

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

float texture_animation_value()
{
    return abs(texture_flags.y);
}

bool uses_scroll_black_key_animation()
{
    return frac(texture_animation_value()) > texture_animation_scroll_mode_threshold;
}

bool uses_clamped_scroll_animation()
{
    return frac(texture_animation_value()) > texture_animation_clamp_mode_threshold;
}

bool has_base_texture_animation()
{
    return texture_flags.y > 1.0f;
}

float2 animated_tex_coord(float2 tex_coord)
{
    if (uses_scroll_black_key_animation())
    {
        float y = tex_coord.y + texture_flags.w;
        if (uses_clamped_scroll_animation())
            return float2(saturate(tex_coord.x), tex_coord.y + frac(texture_flags.w));

        return float2(saturate(tex_coord.x), frac(y));
    }

    return tex_coord;
}

float animated_tex_alpha_scale(float2 tex_coord)
{
    if (!uses_clamped_scroll_animation())
        return 1.0f;

    float y = tex_coord.y + frac(texture_flags.w);
    return y >= 0.0f && y <= 1.0f ? 1.0f : 0.0f;
}

float4 apply_animated_alpha(float4 color)
{
    if (uses_scroll_black_key_animation() && !uses_clamped_scroll_animation())
    {
        float brightness = max(max(color.r, color.g), color.b);
        color.a *= saturate((brightness - texture_animation_black_key_threshold) * texture_animation_black_key_scale);
    }

    return color;
}

float4 RGBToTransparentRgba(float3 rgb)
{
    // Use luminance as the alpha — black becomes fully transparent,
    // brighter colors become more opaque.
    float alpha = dot(rgb, float3(0.2126f, 0.7152f, 0.0722f)); // Rec.709 luma

    // Optional: if you don't want premultiplied output, just return rgb as-is.
    // If your target expects premultiplied alpha (common for compositing),
    // uncomment the next line:
    // rgb *= alpha;

    return float4(rgb, alpha);
}

float4 sample_animated_texture(Texture2D texture_source, float2 tex_coord)
{
    float4 color = texture_source.Sample(model_sampler, animated_tex_coord(tex_coord));
    return RGBToTransparentRgba(color.rgb);
}

vs_output vs_main(vs_input input)
{
    vs_output output;
    float4 world_position = mul(float4(input.position, 1.0f), world);
    float4 projected_position = mul(float4(input.position, 1.0f), world_view_projection);
    output.position = projected_position;
    if (texture_flags.z >= 0.0f)
    {
        const float local_depth_scale = 0.08f;
        float4 projected_origin = mul(float4(0.0f, 0.0f, 0.0f, 1.0f), world_view_projection);
        float vertex_depth = projected_position.z / max(projected_position.w, 0.000001f);
        float origin_depth = projected_origin.z / max(projected_origin.w, 0.000001f);
        output.position.z = output.position.w * saturate(texture_flags.z + (vertex_depth - origin_depth) * local_depth_scale);
    }
    output.tex_coord = input.tex_coord;
    output.world_position = world_position.xyz;
    output.normal = safe_normalize(mul(float4(input.normal, 0.0f), world).xyz, float3(0.0f, 0.0f, 1.0f));
    return output;
}

float4 ps_main(vs_output input) : SV_Target
{
    float4 color = sample_animated_texture(model_texture, input.tex_coord);
    return color.rgba;
}
