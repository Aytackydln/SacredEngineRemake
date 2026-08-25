// Sacred uses a shared grayscale Texture.pak image as the local-light halo
// shape. Instance colour supplies the warm yellow emitter tint.
#pragma vertex vs_sdr
#pragma fragment ps_sdr

struct LightHaloInstance
{
    float2 position;
    float diameter;
    float opacity;
    float3 colour;
    uint shape;
};

StructuredBuffer<LightHaloInstance> instances : register(t0);
Texture2D halo_texture : register(t1);
SamplerState sampler0 : register(s0);

cbuffer LightHaloSceneConstants : register(b0)
{
    float2 viewport_size;
    float night_blend;
    float white_nits;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    nointerpolation float opacity : TEXCOORD1;
    nointerpolation float3 colour : TEXCOORD2;
    nointerpolation uint shape : TEXCOORD3;
};

static const float2 quad_uvs[4] =
{
    float2(0.0f, 0.0f),
    float2(1.0f, 0.0f),
    float2(0.0f, 1.0f),
    float2(1.0f, 1.0f)
};

vertex_output build_vertex(uint vertex_id, uint instance_id)
{
    LightHaloInstance instance = instances[instance_id];
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = instance.position + uv * instance.diameter;
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f);

    vertex_output output;
    output.position = float4(clip, 1.0f, 1.0f);
    output.tex_coord = uv;
    output.opacity = instance.opacity;
    output.colour = instance.colour;
    output.shape = instance.shape;
    return output;
}

vertex_output vs_sdr(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    return build_vertex(vertex_id, instance_id);
}

vertex_output vs_hdr(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    vertex_output output = build_vertex(vertex_id, instance_id);
    // The colour and paper-white value are constant across an instance. Converting
    // here avoids repeating the expensive sRGB -> Rec.2020 -> PQ work per pixel.
    output.colour = SdrTextureToHdr10(output.colour, white_nits);
    return output;
}

float halo_alpha(vertex_output input)
{
    // Invisible authored light volumes are consumed by the terrain/static-sprite
    // lighting shaders and must never become visible procedural particles.
    if (input.shape == 2)
        discard;

    float mask = halo_texture.Sample(sampler0, input.tex_coord).r;
    if (mask <= 1.0f / 255.0f)
        discard;

    // Fire and magic retain a visible local glow during the day. Night makes
    // that glow dominant, but does not switch the emitter itself on or off.
    float lighting_visibility = lerp(0.35f, 1.0f, saturate(night_blend));
    return mask * input.opacity * lighting_visibility;
}

float4 ps_sdr(vertex_output input) : SV_Target
{
    float alpha = halo_alpha(input);
    return float4(input.colour * alpha, alpha);
}

float4 ps_hdr(vertex_output input) : SV_Target
{
    float alpha = halo_alpha(input);
    return float4(input.colour * alpha, alpha);
}
