cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: texture mode, y: signed packed animation, z: unused, w: elapsed seconds
}

Texture2D ModelTexture : register(t0);
Texture2D ModelOverlayTexture : register(t1);
SamplerState ModelSampler : register(s0);

static const float TextureAnimationFramesPerSecond = 12.0f;
static const float TextureAnimationScrollSpeed = 0.25f;
static const float TextureAnimationScrollModeThreshold = 0.05f;
static const float TextureAnimationBlackKeyThreshold = 0.03f;
static const float TextureAnimationBlackKeyScale = 12.0f;
static const float TextureModeHasTextureThreshold = 0.5f;
static const float TextureModeAlphaOverlayMin = 1.5f;
static const float TextureModeAlphaOverlayMax = 2.5f;
static const float TextureModeMultiTextureFillThreshold = 2.5f;
static const float MultiTextureFillAlphaThreshold = 0.85f;
static const float MultiTextureFillAlphaScale = 8.0f;
static const float MultiTextureFillRedThreshold = 0.08f;
static const float MultiTextureFillRedScale = 8.0f;
static const float MultiTextureFillBrightnessThreshold = 0.20f;
static const float MultiTextureFillBrightnessScale = 5.0f;

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
    float3 world_position : TEXCOORD1;
    float3 normal : TEXCOORD2;
};

float3 SafeNormalize(float3 value, float3 fallback)
{
    float length_squared = dot(value, value);
    return length_squared > 0.000001f ? value * rsqrt(length_squared) : fallback;
}

float TextureAnimationValue()
{
    return abs(texture_flags.y);
}

bool UsesScrollBlackKeyAnimation()
{
    return frac(TextureAnimationValue()) > TextureAnimationScrollModeThreshold;
}

bool HasBaseTextureAnimation()
{
    return texture_flags.y > 1.0f;
}

bool HasOverlayTextureAnimation()
{
    return texture_flags.y < -1.0f;
}

bool HasTexture()
{
    return texture_flags.x > TextureModeHasTextureThreshold;
}

bool HasAlphaOverlayTexture()
{
    return texture_flags.x > TextureModeAlphaOverlayMin &&
        texture_flags.x < TextureModeAlphaOverlayMax;
}

bool HasMultiTextureFillOverlay()
{
    return texture_flags.x > TextureModeMultiTextureFillThreshold;
}

float2 AnimatedTexCoord(float2 tex_coord)
{
    if (UsesScrollBlackKeyAnimation())
        return float2(saturate(tex_coord.x), frac(tex_coord.y - texture_flags.w * TextureAnimationScrollSpeed));

    float frame_count = max(1.0f, floor(TextureAnimationValue()));
    if (frame_count <= 1.0f)
        return tex_coord;

    float frame = fmod(floor(texture_flags.w * TextureAnimationFramesPerSecond), frame_count);
    return float2(saturate(tex_coord.x), (saturate(tex_coord.y) + frame) / frame_count);
}

float4 ApplyAnimatedAlpha(float4 color)
{
    if (UsesScrollBlackKeyAnimation())
    {
        float brightness = max(max(color.r, color.g), color.b);
        color.a *= saturate((brightness - TextureAnimationBlackKeyThreshold) * TextureAnimationBlackKeyScale);
    }

    return color;
}

float4 AlphaBlend(float4 base_color, float4 overlay_color)
{
    float inverse_alpha = 1.0f - overlay_color.a;
    float alpha = overlay_color.a + base_color.a * inverse_alpha;
    float3 color = alpha > 0.000001f
        ? (overlay_color.rgb * overlay_color.a + base_color.rgb * base_color.a * inverse_alpha) / alpha
        : 0.0f;
    return float4(color, alpha);
}

float4 SampleOverlayTexture(float2 tex_coord)
{
    return HasOverlayTextureAnimation()
        ? ApplyAnimatedAlpha(ModelOverlayTexture.Sample(ModelSampler, AnimatedTexCoord(tex_coord)))
        : ModelOverlayTexture.Sample(ModelSampler, tex_coord);
}

float MultiTextureFillMask(float4 base_color)
{
    float red_dominance = base_color.r - max(base_color.g, base_color.b);
    float red_mask = saturate((red_dominance - MultiTextureFillRedThreshold) * MultiTextureFillRedScale) *
        saturate((base_color.r - MultiTextureFillBrightnessThreshold) * MultiTextureFillBrightnessScale);
    float alpha_mask = saturate((MultiTextureFillAlphaThreshold - base_color.a) * MultiTextureFillAlphaScale);
    return saturate(max(red_mask, alpha_mask));
}

float4 ApplyMultiTextureFill(float4 base_color, float4 fill_color)
{
    float fill_mask = MultiTextureFillMask(base_color) * fill_color.a;
    base_color.rgb = lerp(base_color.rgb, fill_color.rgb, fill_mask);
    base_color.a = max(base_color.a, fill_mask);
    return base_color;
}

vs_output vs_main(vs_input input)
{
    vs_output output;
    float4 world_position = mul(float4(input.position, 1.0f), world);
    output.position = mul(float4(input.position, 1.0f), world_view_projection);
    output.tex_coord = input.tex_coord;
    output.world_position = world_position.xyz;
    output.normal = SafeNormalize(mul(float4(input.normal, 0.0f), world).xyz, float3(0.0f, 0.0f, 1.0f));
    return output;
}

static const float3 light_direction = float3(0.0f, -1.0f, 0.0f);

float4 ps_main(vs_output input) : SV_Target
{
    float4 base_color = model_color;
    if (HasTexture())
    {
        base_color = HasBaseTextureAnimation()
            ? ApplyAnimatedAlpha(ModelTexture.Sample(ModelSampler, AnimatedTexCoord(input.tex_coord)))
            : ModelTexture.Sample(ModelSampler, input.tex_coord);

        if (HasAlphaOverlayTexture())
            base_color = AlphaBlend(base_color, SampleOverlayTexture(input.tex_coord));
        else if (HasMultiTextureFillOverlay())
            base_color = ApplyMultiTextureFill(base_color, SampleOverlayTexture(input.tex_coord));
    }

    float alpha_cutoff = HasTexture() ? 0.10f : 0.0f;
    if (base_color.a * model_color.a < alpha_cutoff)
        discard;

    float3 normal = SafeNormalize(input.normal, float3(0.0f, 0.0f, 1.0f));
    float diffuse = saturate(dot(normal, light_direction));
    float3 lit = base_color.rgb * (0.32f + diffuse * 0.78f);
    return float4(saturate(lit), base_color.a * model_color.a);
}
