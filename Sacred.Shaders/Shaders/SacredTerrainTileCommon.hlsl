struct TerrainTileInstance
{
    float2 destination_origin;
    float2 primary_source_origin;
    float2 secondary_source_origin;
    uint primary_texture_index;
    uint secondary_texture_index;
    uint flags;
    uint packed_baked_light;
    float4 visual_elevation_samples;
};

// Sacred.exe submits every ground tile as the same four-vertex triangle strip:
// left, top, bottom, right. Expanded here to two independent triangles, retaining
// the native top-to-bottom diagonal.
static const float2 destination_vertices[6] =
{
    float2(0.0f, 24.0f), float2(48.0f, 0.0f), float2(48.0f, 48.0f),
    float2(48.0f, 0.0f), float2(96.0f, 24.0f), float2(48.0f, 48.0f)
};

static const float2 source_vertices[6] =
{
    float2(2.512f, 24.012f), float2(50.512f, 1.012f), float2(50.000f, 48.512f),
    float2(50.512f, 1.012f), float2(98.012f, 23.500f), float2(50.000f, 48.512f)
};

// Corner order matches WLDX 0x10-0x13 after mapping the diamond corners:
// south-west/left, north-west/top, north-east/right, south-east/bottom.
static const uint surface_vertex_indices[6] =
{
    0, 1, 3,
    1, 2, 3
};

float surface_value(float4 corner_values, uint vertex_id)
{
    return corner_values[surface_vertex_indices[vertex_id]];
}

