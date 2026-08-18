cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color; // w: opacity
    float4 texture_flags; // z: fixed painter depth
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 radial_position : TEXCOORD0;
};

static const float2 quad_positions[6] =
{
    float2(-1.0f, -1.0f),
    float2( 1.0f, -1.0f),
    float2(-1.0f,  1.0f),
    float2(-1.0f,  1.0f),
    float2( 1.0f, -1.0f),
    float2( 1.0f,  1.0f)
};

vertex_output vs_main(uint vertex_id : SV_VertexID)
{
    vertex_output output;
    float2 local_position = quad_positions[vertex_id];
    output.position = mul(float4(local_position, 0.0f, 1.0f), world_view_projection);
    output.position.z = output.position.w * texture_flags.z;
    output.radial_position = local_position;
    return output;
}

float4 ps_main(vertex_output input) : SV_Target
{
    float radius = length(input.radial_position);
    if (radius >= 1.0f)
        discard;

    float soft_coverage = 1.0f - smoothstep(0.35f, 1.0f, radius);
    float alpha = model_color.w * soft_coverage;
    return float4(0.0f, 0.0f, 0.0f, alpha);
}
