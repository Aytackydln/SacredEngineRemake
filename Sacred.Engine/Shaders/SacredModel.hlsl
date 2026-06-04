cbuffer ModelConstants : register(b0)
{
    row_major float4x4 world_view_projection;
    row_major float4x4 world;
    float4 model_color;
    float4 texture_flags; // x: has texture, y: local depth scale, z: painter depth, w: alpha cutoff
}

cbuffer SceneConstants : register(b1)
{
    float4 light_position_and_specular_strength;
    float4 camera_position_and_shininess;
    float4 ambient_color_and_intensity;
    float4 light_color_and_diffuse_intensity;
}

Texture2D ModelTexture : register(t0);
SamplerState ModelSampler : register(s0);

struct VsInput
{
    float3 position : POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD;
};

struct VsOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float3 WorldPosition : TEXCOORD1;
    float3 Normal : TEXCOORD2;
};

float3 SafeNormalize(float3 value, float3 fallback)
{
    float length_squared = dot(value, value);
    return length_squared > 0.000001f ? value * rsqrt(length_squared) : fallback;
}

VsOutput vs_main(VsInput input)
{
    VsOutput output;
    float4 world_position = mul(float4(input.position, 1.0f), world);
    float4 projected_position = mul(float4(input.position, 1.0f), world_view_projection);
    output.Position = projected_position;
    if (texture_flags.y > 0.0f)
    {
        float4 projected_origin = mul(float4(0.0f, 0.0f, 0.0f, 1.0f), world_view_projection);
        float vertex_depth = projected_position.z / max(projected_position.w, 0.000001f);
        float origin_depth = projected_origin.z / max(projected_origin.w, 0.000001f);
        output.Position.z = output.Position.w * saturate(texture_flags.z + (vertex_depth - origin_depth) * texture_flags.y);
    }
    output.TexCoord = input.TexCoord;
    output.WorldPosition = world_position.xyz;
    output.Normal = SafeNormalize(mul(float4(input.Normal, 0.0f), world).xyz, float3(0.0f, 0.0f, 1.0f));
    return output;
}

float4 ps_main(VsOutput input) : SV_Target
{
    float4 base_color = model_color;
    if (texture_flags.x > 0.5f)
        base_color = ModelTexture.Sample(ModelSampler, input.TexCoord);

    if (base_color.a * model_color.a < texture_flags.w)
        discard;

    float3 normal = SafeNormalize(input.Normal, float3(0.0f, 0.0f, 1.0f));
    float3 light_position = light_position_and_specular_strength.xyz;
    float3 camera_position = camera_position_and_shininess.xyz;
    float3 light_vector = light_position - input.WorldPosition;
    float3 view_vector = camera_position - input.WorldPosition;
    float3 light_direction = SafeNormalize(light_vector, float3(0.0f, -0.7071f, 0.7071f));
    float3 view_direction = SafeNormalize(view_vector, float3(0.0f, -0.7071f, 0.7071f));

    float diffuse_amount = saturate(dot(normal, light_direction));
    float3 reflection_direction = reflect(-light_direction, normal);
    float specular_amount = diffuse_amount > 0.0f
        ? pow(saturate(dot(reflection_direction, view_direction)), max(camera_position_and_shininess.w, 1.0f))
        : 0.0f;

    float3 ambient = ambient_color_and_intensity.rgb * ambient_color_and_intensity.w;
    float3 diffuse = light_color_and_diffuse_intensity.rgb * (diffuse_amount * light_color_and_diffuse_intensity.w);
    float3 specular = light_color_and_diffuse_intensity.rgb * (specular_amount * light_position_and_specular_strength.w);
    float3 lit_color = base_color.rgb * (ambient + diffuse) + specular;

    return float4(saturate(lit_color), base_color.a * model_color.a);
}
