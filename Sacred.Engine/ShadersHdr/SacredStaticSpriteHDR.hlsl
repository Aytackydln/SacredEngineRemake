// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_main

Texture2D texture0 : register(t0);
SamplerState sampler0 : register(s0);

cbuffer QuadConstants : register(b0)
{
    float4 rect;
    float2 viewport_size;
    float depth;
    float alpha_cutoff;
    float4 hdr_brightness; // x: scene paper white, y: UI paper white, z: UI pass
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
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

vertex_output vs_main(uint vertex_id : SV_VertexID)
{
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = rect.xy + uv * rect.zw;
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f
    );

    vertex_output output;
    output.position = float4(clip, depth, 1.0f);
    output.tex_coord = uv;
    return output;
}

pixel_output ps_main(vertex_output input)
{
    float4 tex = texture0.Sample(sampler0, input.tex_coord);
    if (tex.a < alpha_cutoff)
        discard;

    float paper_white = hdr_brightness.z > 0.5f ? hdr_brightness.y : hdr_brightness.x;

    pixel_output output;
    output.color = float4(SdrTextureToHdr10(tex.rgb, paper_white), tex.a);
    output.depth = depth;
    return output;
}
