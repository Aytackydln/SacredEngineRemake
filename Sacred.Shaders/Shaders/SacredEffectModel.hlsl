// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: packed overlay animation, z: painter depth, w: scaled animation time
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

static const float texture_mode_has_texture_threshold = 0.5f;
static const float texture_mode_multitexture_fill_threshold = 2.5f;
static const float texture_animation_scroll_mode_threshold = 0.25f;
static const float texture_animation_clamp_mode_threshold = 0.60f;
static const float texture_animation_black_key_threshold = 0.03f;
static const float texture_animation_black_key_scale = 12.0f;
static const float effect_alpha_cutoff = 0.015f;
static const float multitexture_fill_alpha_threshold = 0.85f;
static const float multitexture_fill_alpha_scale = 8.0f;
static const float multitexture_fill_red_threshold = 0.08f;
static const float multitexture_fill_red_scale = 8.0f;
static const float multitexture_fill_brightness_threshold = 0.20f;
static const float multitexture_fill_brightness_scale = 5.0f;

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

bool has_texture()
{
    return texture_flags.x > texture_mode_has_texture_threshold;
}

bool has_effect_overlay_texture()
{
    return texture_flags.x > texture_mode_multitexture_fill_threshold;
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

float4 sample_animated_overlay(float2 tex_coord)
{
    float4 color = model_overlay_texture.Sample(model_sampler, animated_tex_coord(tex_coord));
    color.a *= animated_tex_alpha_scale(tex_coord);
    return apply_animated_alpha(color);
}

float multitexture_fill_mask(float4 base_color)
{
    float red_dominance = base_color.r - max(base_color.g, base_color.b);
    float red_mask = saturate((red_dominance - multitexture_fill_red_threshold) * multitexture_fill_red_scale) *
        saturate((base_color.r - multitexture_fill_brightness_threshold) * multitexture_fill_brightness_scale);
    float alpha_mask = saturate((multitexture_fill_alpha_threshold - base_color.a) * multitexture_fill_alpha_scale);
    return saturate(max(red_mask, alpha_mask));
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
    float4 base_color = model_color;
    if (has_texture())
    {
        base_color = model_texture.Sample(model_sampler, input.tex_coord);
    }

    // "glow" effects should not be affected by light, immediately return
    if (base_color.a < effect_alpha_cutoff)
    {
        return sample_animated_overlay(input.tex_coord);
    }

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

    float3 ambient = ambient_color_and_intensity.rgb * ambient_color_and_intensity.w;
    float3 diffuse = light_color_and_diffuse_intensity.rgb * (diffuse_amount * light_color_and_diffuse_intensity.w);
    float3 specular = light_color_and_diffuse_intensity.rgb * (specular_amount * light_position_and_specular_strength.w);
    float3 lit_color = base_color.rgb * (ambient + diffuse) + specular;

    return float4(saturate(lit_color), base_color.a);
}
