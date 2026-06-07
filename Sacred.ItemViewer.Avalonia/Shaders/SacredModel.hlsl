cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags;
}

Texture2D ModelTexture : register(t0);
SamplerState ModelSampler : register(s0);

struct vs_input
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 tex_coord : TEXCOORD;
};

struct vs_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    float3 world_position : TEXCOORD1;
    float3 normal : TEXCOORD2;
};

float3 SafeNormalize(float3 value, float3 fallback)
{
    float length_squared = dot(value, value);
    return length_squared > 0.000001f ? value * rsqrt(length_squared) : fallback;
}

vs_output vs_main(vs_input input)
{
    vs_output output;
    float4 world_position = mul(float4(input.position, 1.0f), world);
    output.position = mul(float4(input.position, 1.0f), world_view_projection);
    output.tex_coord = input.tex_coord;
    output.world_position = world_position.xyz;
    output.normal = SafeNormalize(mul(float4(input.normal, 0.0f), world).xyz, float3(0.0f, 0.0f, 1.0f));
    return output;
}

static const float3 light_direction = float3(0.0f, -1.0f, 0.0f);

float4 ps_main(vs_output input) : SV_Target
{
    float4 base_color = texture_flags.x > 0.5f ? ModelTexture.Sample(ModelSampler, input.tex_coord) : model_color;
    if (base_color.a * model_color.a < texture_flags.w)
        discard;

    float3 normal = SafeNormalize(input.normal, float3(0.0f, 0.0f, 1.0f));
    float diffuse = saturate(dot(normal, light_direction));
    float3 lit = base_color.rgb * (0.32f + diffuse * 0.78f);
    return float4(saturate(lit), base_color.a * model_color.a);
}
