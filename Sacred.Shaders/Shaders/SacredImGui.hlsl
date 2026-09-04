// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_sdr

Texture2D texture0 : register(t0);
SamplerState sampler0 : register(s0);

cbuffer ImGuiConstants : register(b0)
{
    float2 scale;
    float2 translate;
    float paper_white_nits;
}

struct vertex_input
{
    float2 position : POSITION;
    float2 tex_coord : TEXCOORD0;
    float4 color : COLOR0;
};

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    float4 color : COLOR0;
};

vertex_output vs_main(vertex_input input)
{
    vertex_output output;
    output.position = float4(input.position * scale + translate, 0.0f, 1.0f);
    output.tex_coord = input.tex_coord;
    output.color = input.color;
    return output;
}

float4 sample_ui(vertex_output input)
{
    return texture0.Sample(sampler0, input.tex_coord) * input.color;
}

float4 ps_sdr(vertex_output input) : SV_Target
{
    float4 color = sample_ui(input);
    return float4(color.rgb * color.a, color.a);
}

float4 ps_hdr(vertex_output input) : SV_Target
{
    float4 color = sample_ui(input);
    // Keep fully transparent atlas texels at zero. PQ black has a non-zero code
    // value, which otherwise exposes every glyph quad as a bright rectangle.
    return float4(SdrTextureToHdr10(color.rgb, paper_white_nits) * color.a, color.a);
}
