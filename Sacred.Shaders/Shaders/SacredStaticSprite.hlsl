// This shader intentionally targets Shader Model 5.0.  It avoids resource
// arrays, which Proton's D3DCompiler implementation cannot compile.
#pragma vertex vs_main
#pragma fragment ps_main

struct SpriteInstance
{
    float4 rect;
    float depth;
    uint texture_index;
    uint frame_count;
    uint flags;
    float animation_period_seconds;
    float4 corner_alpha;
    uint atlas_columns;
    uint atlas_rows;
    float padding;
};

StructuredBuffer<SpriteInstance> instances : register(t0);
Texture2D static_texture : register(t1);
SamplerState sampler0 : register(s0);

cbuffer StaticSpriteSceneConstants : register(b0)
{
    float2 viewport_size;
    float alpha_cutoff;
    float ambient_intensity;
    float scene_paper_white;
    float unlit_white_nits;
    float animation_time;
}

struct vertex_output
{
    float4 position : SV_Position;
    float2 tex_coord : TEXCOORD0;
    nointerpolation float depth : TEXCOORD1;
    nointerpolation uint frame_count : TEXCOORD2;
    nointerpolation uint flags : TEXCOORD3;
    nointerpolation float animation_period_seconds : TEXCOORD4;
    nointerpolation float4 corner_alpha : TEXCOORD5;
    nointerpolation uint atlas_columns : TEXCOORD6;
    nointerpolation uint atlas_rows : TEXCOORD7;
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
    float2 clip = float2(
        pixel.x / viewport_size.x * 2.0f - 1.0f,
        1.0f - pixel.y / viewport_size.y * 2.0f
    );

    vertex_output output;
    output.position = float4(clip, instance.depth, 1.0f);
    output.tex_coord = uv;
    output.depth = instance.depth;
    output.frame_count = instance.frame_count;
    output.flags = instance.flags;
    output.animation_period_seconds = instance.animation_period_seconds;
    output.corner_alpha = instance.corner_alpha;
    output.atlas_columns = instance.atlas_columns;
    output.atlas_rows = instance.atlas_rows;
    return output;
}

float liquid_corner_alpha(float2 uv, float4 corner_alpha)
{
    float2 liquid_position = uv * 2.0f - 1.0f;
    if (abs(liquid_position.x) + abs(liquid_position.y) > 1.0f)
        discard;

    float center_alpha = dot(corner_alpha, 0.25f);
    float interpolated_alpha;
    if (liquid_position.y < 0.0f)
    {
        if (liquid_position.x < 0.0f)
            interpolated_alpha = center_alpha * (1.0f + liquid_position.x + liquid_position.y) - corner_alpha.x * liquid_position.x - corner_alpha.y * liquid_position.y;
        else
            interpolated_alpha = center_alpha * (1.0f - liquid_position.x + liquid_position.y) + corner_alpha.z * liquid_position.x - corner_alpha.y * liquid_position.y;
    }
    else if (liquid_position.x < 0.0f)
        interpolated_alpha = center_alpha * (1.0f + liquid_position.x - liquid_position.y) - corner_alpha.x * liquid_position.x + corner_alpha.w * liquid_position.y;
    else
        interpolated_alpha = center_alpha * (1.0f - liquid_position.x - liquid_position.y) + corner_alpha.z * liquid_position.x + corner_alpha.w * liquid_position.y;

    return saturate(interpolated_alpha);
}

float4 sample_sprite_texture(Texture2D texture_to_sample, vertex_output input)
{
    if (input.frame_count <= 1 || input.animation_period_seconds <= 0.0f)
        return texture_to_sample.Sample(sampler0, input.tex_coord);

    uint atlas_width;
    uint atlas_height;
    texture_to_sample.GetDimensions(atlas_width, atlas_height);
    uint atlas_columns = max(1, input.atlas_columns);
    uint atlas_rows = max(1, input.atlas_rows);
    uint frame_width = max(1, atlas_width / atlas_columns);
    uint frame_height = max(1, atlas_height / atlas_rows);
    uint elapsed_milliseconds = (uint)(animation_time * 1000.0f);
    uint frame_index;
    if ((input.flags & 1) != 0)
    {
        uint liquid_phase = (elapsed_milliseconds >> 1) & 1023;
        frame_index = (liquid_phase * input.frame_count) >> 10;
    }
    else
    {
        uint animation_period_milliseconds = max(
            1,
            (uint)round(input.animation_period_seconds * 1000.0f));
        frame_index = (elapsed_milliseconds % animation_period_milliseconds) *
            input.frame_count / animation_period_milliseconds;
    }
    frame_index = min(frame_index, input.frame_count - 1);
    uint frame_column = frame_index % atlas_columns;
    uint frame_row = frame_index / atlas_columns;
    float2 frame_uv = input.tex_coord;
    if ((input.flags & 1) != 0)
    {
        // Water frames are authored as top-down 128x128 textures spanning a
        // four-by-four block of terrain cells. Preserve their spatial detail by
        // sampling the appropriate cell region rather than repeating the
        // entire frame on every projected diamond.
        float2 cell_uv = float2(
            input.tex_coord.x + input.tex_coord.y - 0.5f,
            input.tex_coord.y - input.tex_coord.x + 0.5f);
        uint texture_variant = input.flags >> 1;
        float2 block_cell = float2(texture_variant & 3, (texture_variant >> 2) & 3);
        frame_uv = (block_cell + cell_uv) * 0.25f;
    }
    float2 atlas_uv = float2(
        (frame_column * frame_width + 0.5f + frame_uv.x * (frame_width - 1.0f)) / atlas_width,
        (frame_row * frame_height + 0.5f + frame_uv.y * (frame_height - 1.0f)) / atlas_height);
    return texture_to_sample.Sample(sampler0, atlas_uv);
}

pixel_output ps_main(vertex_output input)
{
    bool is_light_halo = (input.flags & 0x40000000u) != 0;
    if (is_light_halo)
    {
        float2 radial_position = input.tex_coord * 2.0f - 1.0f;
        float radius = length(radial_position);
        if (radius >= 1.0f)
            discard;

        float falloff = saturate(1.0f - radius);
        falloff *= falloff;
        float alpha = falloff * input.corner_alpha.w * saturate(ambient_intensity);

        pixel_output halo_output;
        halo_output.color = float4(input.corner_alpha.rgb * alpha, alpha);
        halo_output.depth = input.depth;
        return halo_output;
    }

    float4 color = sample_sprite_texture(static_texture, input);

    if (color.a == 0)
    {
        discard;
    }

    bool is_liquid = (input.flags & 1) != 0;
    if (is_liquid)
        color.a *= liquid_corner_alpha(input.tex_coord, input.corner_alpha);

    if (color.a < (is_liquid ? (1.0f / 255.0f) : alpha_cutoff))
        discard;

    bool is_unlit = !is_liquid && (input.flags & 0x80000000u) != 0;
    color.rgb *= is_unlit ? 1.0f : ambient_intensity;

    pixel_output output;
    output.color = color;
    output.depth = input.depth;
    return output;
}
