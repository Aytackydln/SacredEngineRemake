// Rasterizes a file-authored static sprite into a persistent sector texture.
#pragma vertex vs_main
#pragma fragment ps_main

struct SectorSpriteInstance
{
    float4 destination_rect;
};

StructuredBuffer<SectorSpriteInstance> instances : register(t0);
Texture2D sprite_texture : register(t1);

cbuffer CompositionConstants : register(b0)
{
    float2 target_size;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

static const float2 quad_uvs[6] =
{
    float2(0.0f, 0.0f), float2(1.0f, 0.0f), float2(1.0f, 1.0f),
    float2(0.0f, 0.0f), float2(1.0f, 1.0f), float2(0.0f, 1.0f)
};

vertex_output vs_main(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    SectorSpriteInstance instance = instances[instance_id];
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = instance.destination_rect.xy + uv * instance.destination_rect.zw;
    vertex_output output;
    output.position = float4(
        pixel.x / target_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / target_size.y * 2.0f,
        0.0f,
        1.0f);
    output.uv = uv;
    return output;
}

float4 ps_main(vertex_output input) : SV_Target
{
    uint width;
    uint height;
    sprite_texture.GetDimensions(width, height);
    float2 pixel = input.uv * float2(width - 1u, height - 1u) + 0.5f;
    return sprite_texture.Load(int3((int2)pixel, 0));
}
