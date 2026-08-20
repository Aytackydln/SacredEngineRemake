// Procedural world-light halos deliberately have no texture or sampler binding.
// Keeping this pass separate prevents halo discovery from entering texture residency.
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

cbuffer LightHaloSceneConstants : register(b0)
{
    float2 viewport_size;
    float night_blend;
    float white_nits;
    float animation_time;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    nointerpolation float opacity : TEXCOORD1;
    nointerpolation float3 colour : TEXCOORD2;
    nointerpolation uint shape : TEXCOORD3;
    nointerpolation float3 sparkle_pulses : TEXCOORD4;
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
    output.sparkle_pulses = float3(
        0.58f + 0.42f * sin(animation_time * 4.7f),
        0.62f + 0.38f * sin(animation_time * 3.9f + 2.1f),
        0.56f + 0.44f * sin(animation_time * 5.3f + 4.0f));
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

float star_alpha(float2 position, float pulse)
{
    float radius = length(position);
    float core = saturate(1.0f - radius * 8.0f);
    float axial_distance = min(abs(position.x), abs(position.y));
    float diagonal_distance = min(
        abs(position.x + position.y) * 0.7071f,
        abs(position.x - position.y) * 0.7071f);
    float reach = saturate(1.0f - radius * 2.1f);
    float axial = saturate(1.0f - axial_distance * 30.0f) * reach;
    float diagonal = saturate(1.0f - diagonal_distance * 38.0f) * reach * 0.55f;
    return max(core, max(axial, diagonal)) * pulse;
}

float sparkle_cluster_alpha(vertex_output input)
{
    float2 p = input.tex_coord * 2.0f - 1.0f;
    float stars = star_alpha((p - float2(-0.34f, 0.05f)) / 0.72f, input.sparkle_pulses.x);
    stars = max(stars, star_alpha((p - float2(0.28f, 0.30f)) / 0.56f, input.sparkle_pulses.y));
    stars = max(stars, star_alpha((p - float2(0.08f, -0.50f)) / 0.42f, input.sparkle_pulses.z));
    if (stars <= 0.002f)
        discard;
    return stars * input.opacity;
}

float halo_alpha(vertex_output input)
{
    // Invisible authored light volumes are consumed by the terrain/static-sprite
    // lighting shaders and must never become visible procedural particles.
    if (input.shape == 2)
        discard;

    if (input.shape == 1)
        return sparkle_cluster_alpha(input);

    float2 radial_position = input.tex_coord * 2.0f - 1.0f;
    float radius = length(radial_position);
    if (radius >= 1.0f)
        discard;

    float falloff = saturate(1.0f - radius);
    // Fire and magic retain a visible local glow during the day. Night makes
    // that glow dominant, but does not switch the emitter itself on or off.
    float lighting_visibility = lerp(0.35f, 1.0f, saturate(night_blend));
    return falloff * falloff * input.opacity * lighting_visibility;
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
