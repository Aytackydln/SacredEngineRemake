// On-demand guide rendering uses the same native tile geometry as composition.
StructuredBuffer<TerrainTileInstance> tile_instances : register(t0);
Texture2D guide_texture : register(t1);

cbuffer DebugConstants : register(b0)
{
    float2 target_size;
    float zoom;
    float paper_white_nits; // Zero for SDR output.
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 source_pixel : TEXCOORD0;
    float baked_light : TEXCOORD1;
};

vertex_output vs_main(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    TerrainTileInstance instance = tile_instances[instance_id];
    float elevation = surface_value(instance.visual_elevation_samples, vertex_id);
    float2 pixel = instance.destination_origin +
        (destination_vertices[vertex_id] + float2(0.0f, -elevation)) * zoom;
    vertex_output output;
    output.position = float4(pixel.x / target_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / target_size.y * 2.0f, 0.0f, 1.0f);
    output.source_pixel = instance.primary_source_origin + source_vertices[vertex_id];
    float4 light = float4(instance.packed_baked_light & 255,
        (instance.packed_baked_light >> 8) & 255,
        (instance.packed_baked_light >> 16) & 255,
        (instance.packed_baked_light >> 24) & 255) / 255.0f;
    output.baked_light = surface_value(light, vertex_id);
    return output;
}

float4 ps_main(vertex_output input) : SV_Target
{
    uint width, height;
    guide_texture.GetDimensions(width, height);
    int2 pixel = clamp((int2)round(input.source_pixel), int2(0, 0), int2(width - 1, height - 1));
    float4 color = guide_texture.Load(int3(pixel, 0));
    color.rgb *= input.baked_light;
    if (paper_white_nits > 0.0f)
        color.rgb = SdrTextureToHdr10(color.rgb, paper_white_nits);
    color.rgb *= color.a;
    return color;
}
