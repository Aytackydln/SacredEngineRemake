// #pragma hlsl profile ps_6_6
#pragma vertex vs_main
#pragma fragment ps_sdr

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: signed packed animation, z: painter depth/mode, w: animation time scale
}

cbuffer SceneConstants : register(b1)
{
    float4 light_direction_and_specular_strength;
    float4 camera_position_and_shininess;
    float4 ambient_color_and_intensity;
    float4 light_color_and_diffuse_intensity;
    float4 hdr_display; // x: scene paper white, y: surface-light influence, z: sun diffuse nits, w: sun specular nits
    float scene_elapsed_seconds;
}

Texture2D model_texture : register(t0);
Texture2D model_overlay_texture : register(t1);
Texture2D<float> surface_light_map : register(t2);
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

float surface_light_at(float2 pixel_position)
{
    return surface_light_map.Load(int3(int2(pixel_position), 0)) * hdr_display.y;
}

float3 model_surface_lighting(float2 pixel_position)
{
    float3 ambient = ambient_color_and_intensity.rgb * ambient_color_and_intensity.w;
    return min(ambient + surface_light_at(pixel_position), 1.0f);
}

float3 compose_sdr_model_lighting(float3 base_color, float3 surface_lighting, float3 diffuse, float3 specular)
{
    float3 lit_color = base_color * surface_lighting;
    lit_color += saturate(base_color * diffuse) * (1.0f - lit_color);
    lit_color += saturate(specular) * (1.0f - lit_color);
    return lit_color;
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
    float animation_time = scene_elapsed_seconds * texture_flags.w;
    if (uses_scroll_black_key_animation())
    {
        float y = tex_coord.y + animation_time;
        if (uses_clamped_scroll_animation())
            return float2(saturate(tex_coord.x), tex_coord.y + frac(animation_time));

        return float2(saturate(tex_coord.x), frac(y));
    }

    return tex_coord;
}

float animated_tex_alpha_scale(float2 tex_coord)
{
    if (!uses_clamped_scroll_animation())
        return 1.0f;

    float animation_time = scene_elapsed_seconds * texture_flags.w;
    float y = tex_coord.y + frac(animation_time);
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
    // Use luminance as the alpha ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â black becomes fully transparent,
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

vs_output vs_main(vs_input input
#ifdef SACRED_SKINNING
    , uint vertex_id : SV_VertexID
#endif
)
{
#ifdef SACRED_SKINNING
    skin_vertex(vertex_id, input.position, input.normal, input.tex_coord);
#endif
    vs_output output;
    float4 world_position = mul(float4(input.position, 1.0f), world);
    float4 projected_position = mul(float4(input.position, 1.0f), world_view_projection);
    output.position = projected_position;
    if (texture_flags.z >= 1.0f)
    {
        const float local_depth_per_model_unit = 1.0f / (24.0f * 1.41421356237f * 4096.0f);
        float4 world_origin = mul(float4(0.0f, 0.0f, 0.0f, 1.0f), world);
        float local_depth = ((world_position.y - world_origin.y) -
                             (world_position.z - world_origin.z)) * local_depth_per_model_unit;
        output.position.z = output.position.w * saturate(texture_flags.z - 2.0f + local_depth);
    }
    else if (texture_flags.z >= 0.0f)
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

float4 ps_sdr(vs_output input) : SV_Target
{
    float4 color = sample_animated_texture(model_texture, input.tex_coord);

    float3 normal = safe_normalize(input.normal, float3(0.0f, 0.0f, 1.0f));
    float3 light_direction = safe_normalize(
        light_direction_and_specular_strength.xyz,
        float3(0.0f, -0.7071f, 0.7071f));
    float3 view_direction = safe_normalize(
        camera_position_and_shininess.xyz - input.world_position,
        float3(0.0f, -0.7071f, 0.7071f));
    float diffuse_amount = saturate(dot(normal, light_direction));
    float3 reflection_direction = reflect(-light_direction, normal);
    float specular_amount = diffuse_amount > 0.0f
        ? pow(saturate(dot(reflection_direction, view_direction)), max(camera_position_and_shininess.w, 1.0f))
        : 0.0f;
    float3 surface_lighting = model_surface_lighting(input.position.xy);
    float3 diffuse = light_color_and_diffuse_intensity.rgb *
        (diffuse_amount * light_color_and_diffuse_intensity.w);
    float specular_surface_light = max(surface_lighting.r, max(surface_lighting.g, surface_lighting.b));
    float3 specular = light_color_and_diffuse_intensity.rgb *
        (specular_amount * light_direction_and_specular_strength.w * specular_surface_light);
    return float4(compose_sdr_model_lighting(color.rgb, surface_lighting, diffuse, specular), color.a);
}

float4 ps_hdr(vs_output input) : SV_Target
{
    float4 color = sample_animated_texture(model_texture, input.tex_coord);

    float3 normal = safe_normalize(input.normal, float3(0.0f, 0.0f, 1.0f));
    float3 light_direction = safe_normalize(
        light_direction_and_specular_strength.xyz,
        float3(0.0f, -0.7071f, 0.7071f));
    float3 view_direction = safe_normalize(
        camera_position_and_shininess.xyz - input.world_position,
        float3(0.0f, -0.7071f, 0.7071f));
    float diffuse_amount = saturate(dot(normal, light_direction));
    float3 reflection_direction = reflect(-light_direction, normal);
    float specular_amount = diffuse_amount > 0.0f
        ? pow(saturate(dot(reflection_direction, view_direction)), max(camera_position_and_shininess.w, 1.0f))
        : 0.0f;

    float3 ambient = model_surface_lighting(input.position.xy);
    float3 diffuse = light_color_and_diffuse_intensity.rgb *
        (diffuse_amount * max(light_color_and_diffuse_intensity.w, 0.0f));
    float specular_surface_light = max(ambient.r, max(ambient.g, ambient.b));
    float3 specular = light_color_and_diffuse_intensity.rgb *
        (specular_amount * max(light_direction_and_specular_strength.w, 0.0f) * specular_surface_light);
    float3 hdr = compose_sdr_model_lighting(color.rgb, ambient, diffuse, specular);
    return float4(hdr, color.a);
}
