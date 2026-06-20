// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

Texture2D texture0 : register(t0);
SamplerState sampler0 : register(s0);

cbuffer QuadConstants : register(b0)
{
    float4 rect;
    float2 viewport_size;
    float ambient_intensity;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
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

vertex_output vs_main(uint vertex_id : SV_VertexID)
{
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = rect.xy + uv * rect.zw;
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f
    );

    vertex_output output;
    output.position = float4(clip, 0.0f, 1.0f);
    output.tex_coord = uv;
    return output;
}

float4 ps_main(vertex_output input) : SV_Target
{
    float4 color = texture0.Sample(sampler0, input.tex_coord);
    color.rgb *= ambient_intensity;
    return color;
}
