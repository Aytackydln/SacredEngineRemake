// One-time GPU composition of Sacred's 100x50 atlas cells into 96x48 terrain diamonds.
// Each tile carries absolute indices into the composition context's SRV heap.
#pragma vertex vs_main
#pragma fragment ps_main

static const uint tile_flag_has_secondary_mask = 1;
static const uint tile_flag_premultiplied_output = 2;

StructuredBuffer<TerrainTileInstance> tile_instances : register(t0);

cbuffer CompositionConstants : register(b0)
{
    float2 target_size;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 primary_source_pixel : TEXCOORD0;
    float2 secondary_source_pixel : TEXCOORD1;
    nointerpolation uint flags : TEXCOORD2;
    float baked_light : TEXCOORD3;
    nointerpolation uint primary_texture_index : TEXCOORD4;
    nointerpolation uint secondary_texture_index : TEXCOORD5;
};

vertex_output vs_main(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    TerrainTileInstance instance = tile_instances[instance_id];
    float elevation = surface_value(instance.visual_elevation_samples, vertex_id);
    float2 pixel = instance.destination_origin + destination_vertices[vertex_id] +
                   float2(0.0f, -elevation);
    float2 clip = float2(
        pixel.x / target_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / target_size.y * 2.0f);

    vertex_output output;
    output.position = float4(clip, 0.0f, 1.0f);
    output.primary_source_pixel = instance.primary_source_origin + source_vertices[vertex_id];
    output.secondary_source_pixel = instance.secondary_source_origin + source_vertices[vertex_id];
    output.flags = instance.flags;
    output.primary_texture_index = instance.primary_texture_index;
    output.secondary_texture_index = instance.secondary_texture_index;
    float4 baked_light = float4(
        instance.packed_baked_light & 0xFF,
        (instance.packed_baked_light >> 8) & 0xFF,
        (instance.packed_baked_light >> 16) & 0xFF,
        (instance.packed_baked_light >> 24) & 0xFF) / 255.0f;
    output.baked_light = surface_value(baked_light, vertex_id);
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
    Texture2D primary_texture = ResourceDescriptorHeap[NonUniformResourceIndex(input.primary_texture_index)];
    float4 color = primary_texture.Load(int3(
        clamp_source_pixel(primary_texture, input.primary_source_pixel), 0));

    if ((input.flags & tile_flag_has_secondary_mask) != 0)
    {
        Texture2D secondary_texture = ResourceDescriptorHeap[NonUniformResourceIndex(input.secondary_texture_index)];
        color.a = secondary_texture.Load(int3(
            clamp_source_pixel(secondary_texture, input.secondary_source_pixel), 0)).a;
    }

    if ((input.flags & tile_flag_premultiplied_output) != 0)
        color.rgb *= color.a;

    color.rgb *= input.baked_light;

    return color;
}

// Binary masks can overwrite covered texels without a destination blend read.
float4 ps_opaque(vertex_output input) : SV_Target
{
    float4 color = ps_main(input);
    if (color.a == 0.0f) discard;
    return color;
}
