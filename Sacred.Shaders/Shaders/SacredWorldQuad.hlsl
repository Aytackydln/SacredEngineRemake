// #pragma hlsl profile ps_5_0
#pragma vertex vs_main
#pragma fragment ps_sdr

Texture2D texture0 : register(t0);
Texture2D<float> surface_light_map : register(t1);
SamplerState sampler0 : register(s0);

cbuffer QuadConstants : register(b0)
{
    float4 rect;
    float2 viewport_size;
    float premultiplied_alpha;
    float paper_white_nits;
    float3 ambient_colour;
    float constants_padding;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
};

float surface_lighting(float2 pixel_position)
{
    return surface_light_map.Sample(sampler0, pixel_position / viewport_size);
}

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

float4 ps_sdr(vertex_output input) : SV_Target
{
    float4 color = texture0.Sample(sampler0, input.tex_coord);
    color.rgb *= min(ambient_colour + surface_lighting(input.position.xy), 1.0f);
    return color;
}

float4 ps_sdr_screen(vertex_output input) : SV_Target
{
    return texture0.Sample(sampler0, input.tex_coord);
}

float4 ps_hdr(vertex_output input) : SV_Target
{
    float4 tex = texture0.Sample(sampler0, input.tex_coord);
    tex.rgb *= min(ambient_colour + surface_lighting(input.position.xy), 1.0f);
    if (premultiplied_alpha > 0.5f)
    {
        float3 straight_color = tex.a > 0.0f ? tex.rgb / tex.a : 0.0f;
        return float4(SdrTextureToHdr10(straight_color, paper_white_nits) * tex.a, tex.a);
    }

    return float4(SdrTextureToHdr10(tex.rgb, paper_white_nits), tex.a);
}

float4 ps_hdr_screen(vertex_output input) : SV_Target
{
    float4 tex = texture0.Sample(sampler0, input.tex_coord);
    if (premultiplied_alpha > 0.5f)
    {
        float3 straight_color = tex.a > 0.0f ? tex.rgb / tex.a : 0.0f;
        return float4(SdrTextureToHdr10(straight_color, paper_white_nits) * tex.a, tex.a);
    }

    return float4(SdrTextureToHdr10(tex.rgb, paper_white_nits), tex.a);
}
