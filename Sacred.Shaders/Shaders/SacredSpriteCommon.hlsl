// This shader intentionally targets Shader Model 5.0.  It avoids resource
// arrays, which Proton's D3DCompiler implementation cannot compile.

struct SpriteInstance
{
    float4 rect;
    float depth;
    uint texture_index;
    uint frame_count;
    uint texture_variant;
    uint transpose_texture;
    uint mixed_light_emitter;
    uint particle_sprite;
    uint player_occlusion_fade;
    float animation_period_seconds;
    float4 corner_alpha;
    uint atlas_columns;
    uint atlas_rows;
    float particle_rotation;
};

StructuredBuffer<SpriteInstance> instances : register(t0);
Texture2D static_texture : register(t1);
Texture2D<float> surface_light_map : register(t2);
Texture2D<float> player_occlusion_map : register(t3);
SamplerState sampler0 : register(s0);

cbuffer StaticSpriteSceneConstants : register(b0)
{
    float2 viewport_size;
    float alpha_cutoff;
    float animation_time;
    float3 ambient_colour;
    float scene_paper_white;
    float unlit_white_nits;
    float occluder_opacity;
    float player_scene_depth;
    float constants_padding;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    nointerpolation float depth : TEXCOORD1;
    nointerpolation uint frame_count : TEXCOORD2;
    nointerpolation uint texture_variant : TEXCOORD3;
    nointerpolation uint transpose_texture : TEXCOORD4;
    nointerpolation uint mixed_light_emitter : TEXCOORD5;
    nointerpolation uint particle_sprite : TEXCOORD6;
    nointerpolation uint player_occlusion_fade : TEXCOORD7;
    nointerpolation float animation_period_seconds : TEXCOORD8;
    nointerpolation float4 corner_alpha : TEXCOORD9;
    nointerpolation uint atlas_columns : TEXCOORD10;
    nointerpolation uint atlas_rows : TEXCOORD11;
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

vertex_output vs_main(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
{
    SpriteInstance instance = instances[instance_id];
    float2 uv = quad_uvs[vertex_id];
    float2 pixel = instance.rect.xy + uv * instance.rect.zw;
    if (instance.particle_sprite != 0)
    {
        float sine;
        float cosine;
        sincos(instance.particle_rotation, sine, cosine);
        float2 local = (uv - 0.5f) * instance.rect.zw;
        pixel = instance.rect.xy + instance.rect.zw * 0.5f +
            float2(cosine * local.x - sine * local.y, sine * local.x + cosine * local.y);
    }
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f
    );

    vertex_output output;
    output.position = float4(clip, instance.depth, 1.0f);
    output.tex_coord = uv;
    output.depth = instance.depth;
    output.frame_count = instance.frame_count;
    output.texture_variant = instance.texture_variant;
    output.transpose_texture = instance.transpose_texture;
    output.mixed_light_emitter = instance.mixed_light_emitter;
    output.particle_sprite = instance.particle_sprite;
    output.player_occlusion_fade = instance.player_occlusion_fade;
    output.animation_period_seconds = instance.animation_period_seconds;
    output.corner_alpha = instance.corner_alpha;
    output.atlas_columns = instance.atlas_columns;
    output.atlas_rows = instance.atlas_rows;
    return output;
}
