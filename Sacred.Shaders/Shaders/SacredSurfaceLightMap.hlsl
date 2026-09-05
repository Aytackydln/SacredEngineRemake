#pragma vertex vs_main
#pragma fragment ps_main

struct SurfaceLightInstance
{
    float2 position;
    float diameter;
    float opacity;
    float3 colour;
    uint shape;
};

StructuredBuffer<SurfaceLightInstance> instances : register(t0);

cbuffer SurfaceLightMapSceneConstants : register(b0)
{
    float2 viewport_size;
    float night_blend;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    nointerpolation float opacity : TEXCOORD1;
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
    SurfaceLightInstance instance = instances[instance_id];
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = instance.position + uv * instance.diameter;
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f);

    vertex_output output;
    output.position = float4(clip, 1.0f, 1.0f);
    output.tex_coord = uv;
    output.opacity = instance.opacity;
    return output;
}

float ps_main(vertex_output input) : SV_Target
{
    float2 centered = input.tex_coord * 2.0f - 1.0f;
    float radius = length(centered);
    float falloff = 1.0f - smoothstep(0.12f, 1.0f, radius);
    float night_visibility = saturate(night_blend);
    return falloff * input.opacity * night_visibility;
}
