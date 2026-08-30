#pragma vertex vs_main
#pragma fragment ps_main

static const uint shadow_atlas_cell_mask = 0x000000FFu;
static const uint directional_shadow_flag = 0x00000100u;
static const float shadow_atlas_grid_size = 16.0f;

struct ShadowInstance
{
    // xy: authored ground anchor, z: contact extent, w: projection length.
    float4 geometry;
    uint atlas_cell_and_projection;
};

StructuredBuffer<ShadowInstance> instances : register(t0);
Texture2D shadow_atlas : register(t1);
SamplerState sampler0 : register(s0);

cbuffer StaticSpriteShadowSceneConstants : register(b0)
{
    float2 viewport_size;
    float shadow_opacity;
    float padding;
    // Screen-space direction and solar-elevation length factor, in authored extents.
    float2 directional_projection;
    // Supplied by the CPU so atlas addressing is resolved once per vertex, not per pixel.
    float2 atlas_texel_size;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 atlas_uv : TEXCOORD0;
    nointerpolation float opacity_scale : TEXCOORD1;
};

static const float2 quad_uvs[4] =
{
    float2(0.0f, 0.0f),
    float2(1.0f, 0.0f),
    float2(0.0f, 1.0f),
    float2(1.0f, 1.0f)
};

vertex_output vs_main(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    ShadowInstance instance = instances[instance_id];
    float2 uv = quad_uvs[vertex_id];
    bool directional =
        (instance.atlas_cell_and_projection & directional_shadow_flag) != 0u;
    float2 projection = directional
        ? directional_projection * instance.geometry.w
        : float2(0.0f, -instance.geometry.w);
    float2 pixel = instance.geometry.xy +
        float2((uv.x * 2.0f - 1.0f) * instance.geometry.z, 0.0f) +
        (1.0f - uv.y) * projection;
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f);

    vertex_output output;
    output.position = float4(clip, 0.999f, 1.0f);
    uint cell_index = instance.atlas_cell_and_projection & shadow_atlas_cell_mask;
    float2 cell = float2(cell_index % 16u, cell_index / 16u);
    float2 cell_origin = cell / shadow_atlas_grid_size;
    float2 cell_span = 1.0f / shadow_atlas_grid_size;
    output.atlas_uv = cell_origin + atlas_texel_size * 0.5f +
        uv * (cell_span - atlas_texel_size);
    output.opacity_scale = directional ? 1.08f : 0.86f;
    return output;
}

float4 ps_main(vertex_output input) : SV_Target
{
    float alpha = shadow_atlas.Sample(sampler0, input.atlas_uv).a;
    clip(alpha - (1.0f / 255.0f));
    return float4(
        0.0f,
        0.0f,
        0.0f,
        saturate(shadow_opacity * input.opacity_scale * alpha));
}
