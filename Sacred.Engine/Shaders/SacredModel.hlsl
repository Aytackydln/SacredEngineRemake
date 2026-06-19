// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: signed packed animation, z: painter depth, w: elapsed seconds
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

static const float texture_animation_frames_per_second = 4.0f;
static const float texture_animation_scroll_speed = 0.25f;
static const float texture_animation_scroll_mode_threshold = 0.25f;
static const float texture_animation_black_key_threshold = 0.03f;
static const float texture_animation_black_key_scale = 12.0f;
static const float texture_mode_has_texture_threshold = 0.5f;
static const float texture_mode_alpha_overlay_min = 1.5f;
static const float texture_mode_alpha_overlay_max = 2.5f;
static const float texture_mode_multitexture_fill_threshold = 2.5f;
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

float texture_animation_value()
{
    return abs(texture_flags.y);
}

bool uses_scroll_black_key_animation()
{
    return frac(texture_animation_value()) > texture_animation_scroll_mode_threshold;
}

bool has_base_texture_animation()
{
    return texture_flags.y > 1.0f;
}

bool has_overlay_texture_animation()
{
    return texture_flags.y < -1.0f;
}

bool has_texture()
{
    return texture_flags.x > texture_mode_has_texture_threshold;
}

bool has_alpha_overlay_texture()
{
    return texture_flags.x > texture_mode_alpha_overlay_min &&
        texture_flags.x < texture_mode_alpha_overlay_max;
}

bool has_multitexture_fill_overlay()
{
    return texture_flags.x > texture_mode_multitexture_fill_threshold;
}

float2 animated_tex_coord(float2 tex_coord)
{
    if (uses_scroll_black_key_animation())
        return float2(saturate(tex_coord.x), frac(tex_coord.y - texture_flags.w * texture_animation_scroll_speed));

    float frame_count = max(1.0f, floor(texture_animation_value()));
    if (frame_count <= 1.0f)
        return tex_coord;

    float frame = fmod(floor(texture_flags.w * texture_animation_frames_per_second), frame_count);
    return float2(saturate(tex_coord.x), (saturate(tex_coord.y) + frame) / frame_count);
}

float4 apply_animated_alpha(float4 color)
{
    if (uses_scroll_black_key_animation())
    {
        float brightness = max(max(color.r, color.g), color.b);
        color.a *= saturate((brightness - texture_animation_black_key_threshold) * texture_animation_black_key_scale);
    }

    return color;
}

float4 alpha_blend(float4 base_color, float4 overlay_color)
{
    float inverse_alpha = 1.0f - overlay_color.a;
    float alpha = overlay_color.a + base_color.a * inverse_alpha;
    float3 color = alpha > 0.000001f
        ? (overlay_color.rgb * overlay_color.a + base_color.rgb * base_color.a * inverse_alpha) / alpha
        : 0.0f;
    return float4(color, alpha);
}

float4 sample_overlay_texture(float2 tex_coord)
{
    return has_overlay_texture_animation()
        ? apply_animated_alpha(model_overlay_texture.Sample(model_sampler, animated_tex_coord(tex_coord)))
        : model_overlay_texture.Sample(model_sampler, tex_coord);
}

float multitexture_fill_mask(float4 base_color)
{
    float red_dominance = base_color.r - max(base_color.g, base_color.b);
    float red_mask = saturate((red_dominance - multitexture_fill_red_threshold) * multitexture_fill_red_scale) *
        saturate((base_color.r - multitexture_fill_brightness_threshold) * multitexture_fill_brightness_scale);
    float alpha_mask = saturate((multitexture_fill_alpha_threshold - base_color.a) * multitexture_fill_alpha_scale);
    return saturate(max(red_mask, alpha_mask));
}

float4 apply_multitexture_fill(float4 base_color, float4 fill_color)
{
    float fill_mask = multitexture_fill_mask(base_color) * fill_color.a;
    base_color.rgb = lerp(base_color.rgb, fill_color.rgb, fill_mask);
    base_color.a = max(base_color.a, fill_mask);
    return base_color;
}

vs_output vs_main(vs_input input)
{
    vs_output output;
    float4 world_position = mul(float4(input.position, 1.0f), world);
    float4 projected_position = mul(float4(input.position, 1.0f), world_view_projection);
    output.position = projected_position;
    const float local_depth_scale = 0.08f;
    if (local_depth_scale > 0.0f)
    {
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

        if (has_alpha_overlay_texture() && !has_overlay_texture_animation())
            base_color = alpha_blend(base_color, model_overlay_texture.Sample(model_sampler, input.tex_coord));
        else if (has_multitexture_fill_overlay())
        {
            if (!has_overlay_texture_animation())
                base_color = apply_multitexture_fill(base_color, model_overlay_texture.Sample(model_sampler, input.tex_coord));
        }
    }

    float alpha_cutoff = has_texture() ? 0.10f : 0.0f;
    if (base_color.a * model_color.a < alpha_cutoff)
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

    float3 ambient = ambient_color_and_intensity.rgb * ambient_color_and_intensity.w;
    float3 diffuse = light_color_and_diffuse_intensity.rgb * (diffuse_amount * light_color_and_diffuse_intensity.w);
    float3 specular = light_color_and_diffuse_intensity.rgb * (specular_amount * light_position_and_specular_strength.w);
    float3 lit_color = base_color.rgb * (ambient + diffuse) + specular;

    return float4(saturate(lit_color), base_color.a * model_color.a);
}

float4 ps_animated_main(vs_output input) : SV_Target
{
    float4 color = 0.0f;

    if (has_base_texture_animation())
    {
        color = apply_animated_alpha(model_texture.Sample(model_sampler, animated_tex_coord(input.tex_coord)));
    }
    else if (has_overlay_texture_animation())
    {
        float4 overlay_color = apply_animated_alpha(model_overlay_texture.Sample(model_sampler, animated_tex_coord(input.tex_coord)));
        if (has_multitexture_fill_overlay())
        {
            float4 base_color = model_texture.Sample(model_sampler, input.tex_coord);
            overlay_color.a *= multitexture_fill_mask(base_color);
        }

        color = overlay_color;
    }

    if (color.a * model_color.a < 0.01f)
        discard;

    return float4(saturate(color.rgb), color.a * model_color.a);
}
