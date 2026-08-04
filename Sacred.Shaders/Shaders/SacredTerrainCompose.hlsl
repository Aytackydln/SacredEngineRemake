// One-time GPU composition of Sacred's 100x50 atlas cells into 96x48 terrain diamonds.
// #pragma hlsl profile ps_5_1
#pragma vertex vs_main
#pragma fragment ps_main

static const uint tile_flag_has_secondary_mask = 1;
static const uint tile_flag_premultiplied_output = 2;

struct TerrainTileInstance
{
    float2 destination_origin;
    float2 primary_source_origin;
    float2 secondary_source_origin;
    uint primary_texture_index;
    uint secondary_texture_index;
    uint flags;
    uint padding0;
    float2 padding1;
};

StructuredBuffer<TerrainTileInstance> tile_instances : register(t0);
Texture2D tile_textures[4096] : register(t1);

cbuffer CompositionConstants : register(b0)
{
    float2 target_size;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 primary_source_pixel : TEXCOORD0;
    float2 secondary_source_pixel : TEXCOORD1;
    nointerpolation uint primary_texture_index : TEXCOORD2;
    nointerpolation uint secondary_texture_index : TEXCOORD3;
    nointerpolation uint flags : TEXCOORD4;
};

static const float2 destination_vertices[12] =
{
    float2(48.0f, 24.0f), float2(0.0f, 24.0f),  float2(48.0f, 0.0f),
    float2(48.0f, 24.0f), float2(48.0f, 0.0f),  float2(96.0f, 24.0f),
    float2(48.0f, 24.0f), float2(96.0f, 24.0f), float2(48.0f, 48.0f),
    float2(48.0f, 24.0f), float2(48.0f, 48.0f), float2(0.0f, 24.0f)
};

static const float2 source_vertices[12] =
{
    float2(50.259f, 24.259f), float2(2.512f, 24.012f),  float2(50.512f, 1.012f),
    float2(50.259f, 24.259f), float2(50.512f, 1.012f),  float2(98.012f, 23.500f),
    float2(50.259f, 24.259f), float2(98.012f, 23.500f), float2(50.000f, 48.512f),
    float2(50.259f, 24.259f), float2(50.000f, 48.512f), float2(2.512f, 24.012f)
};

vertex_output vs_main(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    TerrainTileInstance instance = tile_instances[instance_id];
    float2 pixel = instance.destination_origin + destination_vertices[vertex_id];
    float2 clip = float2(
        pixel.x / target_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / target_size.y * 2.0f);

    vertex_output output;
    output.position = float4(clip, 0.0f, 1.0f);
    output.primary_source_pixel = instance.primary_source_origin + source_vertices[vertex_id];
    output.secondary_source_pixel = instance.secondary_source_origin + source_vertices[vertex_id];
    output.primary_texture_index = instance.primary_texture_index;
    output.secondary_texture_index = instance.secondary_texture_index;
    output.flags = instance.flags;
    return output;
}

int2 clamp_source_pixel(Texture2D texture_to_sample, float2 source_pixel)
{
    uint width;
    uint height;
    texture_to_sample.GetDimensions(width, height);
    return clamp((int2)round(source_pixel), int2(0, 0), int2(width - 1, height - 1));
}

float4 ps_main(vertex_output input) : SV_Target
{
    uint primary_index = NonUniformResourceIndex(input.primary_texture_index);
    float4 color = tile_textures[primary_index].Load(int3(
        clamp_source_pixel(tile_textures[primary_index], input.primary_source_pixel), 0));

    if ((input.flags & tile_flag_has_secondary_mask) != 0)
    {
        uint secondary_index = NonUniformResourceIndex(input.secondary_texture_index);
        color.a = tile_textures[secondary_index].Load(int3(
            clamp_source_pixel(tile_textures[secondary_index], input.secondary_source_pixel), 0)).a;
    }

    if ((input.flags & tile_flag_premultiplied_output) != 0)
        color.rgb *= color.a;

    return color;
}
