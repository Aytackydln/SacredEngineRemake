// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_sdr

cbuffer UiConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 ui_color;
    float4 ui_flags; // x: UI paper white nits
}

struct vs_input
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 tex_coord : TEXCOORD;
};

struct vs_output
{
    float4 position : SV_Position;
};

vs_output vs_main(vs_input input)
{
    vs_output output;
    output.position = mul(float4(input.position, 1.0f), world_view_projection);
    return output;
}

float4 ps_sdr(vs_output input) : SV_Target
{
    return ui_color;
}

float4 ps_hdr(vs_output input) : SV_Target
{
    float paper_white = ui_flags.x > 0.0f ? ui_flags.x : 203.0f;
    return float4(SdrTextureToHdr10(ui_color.rgb, paper_white), ui_color.a);
}
