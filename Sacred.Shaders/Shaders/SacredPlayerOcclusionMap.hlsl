#pragma vertex vs_main
#pragma fragment ps_main

cbuffer PlayerOcclusionMapSceneConstants : register(b0)
{
    float2 viewport_size;
    float2 player_screen_position;
    float occluder_radius_pixels;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
};

static const float2 quad_uvs[4] =
{
    float2(0.0f, 0.0f),
    float2(1.0f, 0.0f),
    float2(0.0f, 1.0f),
    float2(1.0f, 1.0f)
};

vertex_output vs_main(uint vertex_id : SV_VertexID)
{
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = player_screen_position + (uv * 2.0f - 1.0f) * occluder_radius_pixels;

    vertex_output output;
    output.position = float4(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f,
        1.0f,
        1.0f);
    output.tex_coord = uv;
    return output;
}

float ps_main(vertex_output input) : SV_Target
{
    float radius = length(input.tex_coord * 2.0f - 1.0f);
    return 1.0f - smoothstep(0.65f, 1.0f, radius);
}
