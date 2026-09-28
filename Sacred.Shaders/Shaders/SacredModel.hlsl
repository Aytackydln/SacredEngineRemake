// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_sdr

cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: unused, z: painter depth/mode, w: unused
}

cbuffer SceneConstants : register(b1)
{
    float4 light_direction_and_specular_strength;
    float4 camera_position_and_shininess;
    float4 ambient_color_and_intensity;
    float4 light_color_and_diffuse_intensity;
    float4 hdr_display; // x: scene paper white, y: surface-light influence, z: sun diffuse nits, w: sun specular nits
}

Texture2D model_texture : register(t0);
Texture2D model_overlay_texture : register(t1);
Texture2D<float> surface_light_map : register(t2);
SamplerState model_sampler : register(s0);

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
    // Screen the directional terms into the remaining SDR headroom. Add-then-saturate
    // flattens bright texture detail and turns specular highlights into clipped white.
    float3 lit_color = base_color * surface_lighting;
    lit_color += saturate(base_color * diffuse) * (1.0f - lit_color);
    lit_color += saturate(specular) * (1.0f - lit_color);
    return lit_color;
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

float4 alpha_blend(float4 base_color, float4 overlay_color)
{
    float inverse_alpha = 1.0f - overlay_color.a;
    float alpha = overlay_color.a + base_color.a * inverse_alpha;
    float3 color = alpha > 0.000001f
        ? (overlay_color.rgb * overlay_color.a + base_color.rgb * base_color.a * inverse_alpha) / alpha
        : 0.0f;
    return float4(color, alpha);
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
    if (texture_flags.z >= 1.0f)
    {
        // Model-camera Y-Z maps back to the terrain's X+Y tile diagonal at
        // 24*sqrt(2) model units per tile. Keep the authored anchor as the
        // base, then let each vertex cross painter slots with its true depth.
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
    float4 base_color = model_color;
    if (has_texture())
    {
        base_color = model_texture.Sample(model_sampler, input.tex_coord);

        if (has_alpha_overlay_texture())
            base_color = alpha_blend(base_color, model_overlay_texture.Sample(model_sampler, input.tex_coord));
        else if (has_multitexture_fill_overlay())
        {
            base_color = apply_multitexture_fill(base_color, model_overlay_texture.Sample(model_sampler, input.tex_coord));
        }
    }

    if (base_color.a < 0.10f)
        discard;

    float3 normal = input.normal;
    float3 light_direction = normalize(light_direction_and_specular_strength.xyz);
    float diffuse_amount = dot(normal, light_direction);

    float3 surface_lighting = model_surface_lighting(input.position.xy);
    float3 diffuse = light_color_and_diffuse_intensity.rgb * (diffuse_amount * light_color_and_diffuse_intensity.w);
    float3 specular = 0;
    float3 lit_color = compose_sdr_model_lighting(base_color.rgb, surface_lighting, diffuse, specular);

    return float4(lit_color, base_color.a);
}

float4 ps_hdr(vs_output input) : SV_Target
{
    float4 base_color = model_color;
    if (has_texture())
    {
        base_color = model_texture.Sample(model_sampler, input.tex_coord);

        if (has_alpha_overlay_texture())
            base_color = alpha_blend(base_color, model_overlay_texture.Sample(model_sampler, input.tex_coord));
        else if (has_multitexture_fill_overlay())
            base_color = apply_multitexture_fill(base_color, model_overlay_texture.Sample(model_sampler, input.tex_coord));
    }

    if (base_color.a < 0.10f)
        discard;

    float3 normal = input.normal;
    float3 light_direction = normalize(light_direction_and_specular_strength.xyz);
    float diffuse_amount = dot(normal, light_direction);

    float3 ambient = model_surface_lighting(input.position.xy);
    float3 diffuse = light_color_and_diffuse_intensity.rgb * (diffuse_amount * light_color_and_diffuse_intensity.w);
    float3 specular = 0;
    float3 hdr = SdrLitTextureToHdr10(
        base_color.rgb,
        ambient,
        diffuse,
        specular,
        hdr_display.x,
        hdr_display.z,
        hdr_display.w);
    return float4(hdr, base_color.a);
}
