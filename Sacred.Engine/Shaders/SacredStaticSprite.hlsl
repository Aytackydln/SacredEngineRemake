// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

struct SpriteInstance
{
    float4 rect;
    float depth;
    uint texture_index;
    float2 padding;
};

StructuredBuffer<SpriteInstance> instances : register(t0);
Texture2D static_textures[4096] : register(t1);
SamplerState sampler0 : register(s0);

cbuffer StaticSpriteSceneConstants : register(b0)
{
    float2 viewport_size;
    float alpha_cutoff;
    float scene_paper_white;
    float ui_paper_white;
    float ui_pass;
    float2 padding;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    nointerpolation uint texture_index : TEXCOORD1;
    nointerpolation float depth : TEXCOORD2;
};

struct pixel_output
{
    float4 color : SV_Target;
    float depth : SV_Depth;
};

static const float2 quad_uvs[6] =
{
    float2(0.0f, 0.0f),
    float2(1.0f, 0.0f),
    float2(0.0f, 1.0f),
    float2(0.0f, 1.0f),
    float2(1.0f, 0.0f),
    float2(1.0f, 1.0f)
};

vertex_output vs_main(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    SpriteInstance instance = instances[instance_id];
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = instance.rect.xy + uv * instance.rect.zw;
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f
    );

    vertex_output output;
    output.position = float4(clip, instance.depth, 1.0f);
    output.tex_coord = uv;
    output.texture_index = instance.texture_index;
    output.depth = instance.depth;
    return output;
}

pixel_output ps_main(vertex_output input)
{
    uint texture_index = NonUniformResourceIndex(input.texture_index);
    float4 color = static_textures[texture_index].Sample(sampler0, input.tex_coord);
    if (color.a < alpha_cutoff)
        discard;

    pixel_output output;
    output.color = color;
    output.depth = input.depth;
    return output;
}
