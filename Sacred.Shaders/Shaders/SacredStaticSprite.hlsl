// This shader intentionally targets Shader Model 5.0.  It avoids resource
// arrays, which Proton's D3DCompiler implementation cannot compile.
#pragma vertex vs_main
#pragma fragment ps_sdr

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
};

StructuredBuffer<SpriteInstance> instances : register(t0);
Texture2D static_texture : register(t1);
struct WorldLight
{
    float2 position;
    float diameter;
    float opacity;
    float3 colour;
    uint shape;
};
StructuredBuffer<WorldLight> world_lights : register(t2);
SamplerState sampler0 : register(s0);

cbuffer StaticSpriteSceneConstants : register(b0)
{
    float2 viewport_size;
    float alpha_cutoff;
    float world_light_count;
    float3 ambient_colour;
    float scene_paper_white;
    float unlit_white_nits;
    float animation_time;
    float night_blend;
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

float3 surface_lighting(float2 pixel_position)
{
    float3 lighting = ambient_colour;
    uint count = min((uint)(world_light_count + 0.5f), 64u);
    float night_visibility = lerp(0.10f, 1.0f, saturate(night_blend));
    for (uint index = 0; index < count; index++)
    {
        WorldLight light = world_lights[index];
        if (light.shape != 2u || light.diameter <= 0.0f)
            continue;

        float radius = length(
            (pixel_position - (light.position + light.diameter * 0.5f)) /
            (light.diameter * 0.5f));
        float falloff = 1.0f - smoothstep(0.12f, 1.0f, radius);
        // Sacred's surface light map stores intensity, not emitter hue. The
        // visible flame/magic sprite remains coloured in the separate halo pass.
        lighting += falloff * light.opacity * night_visibility;
    }

    return min(lighting, 1.0f);
}

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

int positive_remainder(int value, int divisor)
{
    int remainder = value % divisor;
    return remainder < 0 ? remainder + divisor : remainder;
}

float4 sample_repeating_frame(
    Texture2D texture_to_sample,
    uint frame_column,
    uint frame_row,
    uint frame_width,
    uint frame_height,
    float2 frame_uv)
{
    // Liquid frames repeat in world space. Sampling half a texel inside each
    // frame made the first and last rows meet as a hard 4x4-cell square. Wrap
    // the bilinear taps inside the current animation frame instead; wrapping
    // the atlas sampler itself would bleed into neighbouring animation frames.
    float2 frame_pixel = frame_uv * float2(frame_width, frame_height) - 0.5f;
    int2 lower = (int2)floor(frame_pixel);
    float2 weight = frac(frame_pixel);
    int2 upper = lower + 1;
    int2 frame_origin = int2(frame_column * frame_width, frame_row * frame_height);
    int2 lower_wrapped = int2(
        positive_remainder(lower.x, (int)frame_width),
        positive_remainder(lower.y, (int)frame_height));
    int2 upper_wrapped = int2(
        positive_remainder(upper.x, (int)frame_width),
        positive_remainder(upper.y, (int)frame_height));
    float4 top_left = texture_to_sample.Load(int3(frame_origin + lower_wrapped, 0));
    float4 top_right = texture_to_sample.Load(int3(
        frame_origin + int2(upper_wrapped.x, lower_wrapped.y), 0));
    float4 bottom_left = texture_to_sample.Load(int3(
        frame_origin + int2(lower_wrapped.x, upper_wrapped.y), 0));
    float4 bottom_right = texture_to_sample.Load(int3(frame_origin + upper_wrapped, 0));
    return lerp(
        lerp(top_left, top_right, weight.x),
        lerp(bottom_left, bottom_right, weight.x),
        weight.y);
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
    if ((input.flags & 2) != 0)
        frame_uv = input.tex_coord.yx;
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
        return sample_repeating_frame(
            texture_to_sample,
            frame_column,
            frame_row,
            frame_width,
            frame_height,
            frame_uv);
    }
    float2 atlas_uv = float2(
        (frame_column * frame_width + 0.5f + frame_uv.x * (frame_width - 1.0f)) / atlas_width,
        (frame_row * frame_height + 0.5f + frame_uv.y * (frame_height - 1.0f)) / atlas_height);
    return texture_to_sample.Sample(sampler0, atlas_uv);
}

float mixed_light_emission(float3 colour)
{
    // Class-9 mixed sprites can contain blue magical emitters or orange fire.
    // Preserve only those authored emitter pixels (plus white-hot glints), while
    // their fixture art continues to receive ambient and local surface light.
    float blue_dominance = saturate((colour.b - colour.r - 0.04f) * 6.0f);
    float warm_dominance = saturate((colour.r - colour.b - 0.10f) * 4.0f) *
        saturate((max(colour.r, colour.g) - 0.55f) * 4.0f);
    float white_glint = saturate((max(colour.r, max(colour.g, colour.b)) - 0.80f) * 5.0f);
    return max(max(blue_dominance, warm_dominance), white_glint);
}

pixel_output ps_sdr(vertex_output input)
{
    float4 color = sample_sprite_texture(static_texture, input);

    if (color.a == 0)
    {
        discard;
    }

    bool is_liquid = (input.flags & 1) != 0;
    if (is_liquid)
        color.a *= liquid_corner_alpha(input.tex_coord, input.corner_alpha);

    bool is_particle = !is_liquid && (input.flags & 0x40000000u) != 0;
    bool is_mixed_light = !is_liquid && (input.flags & 0x20000000u) != 0;
    if (is_particle)
    {
        // Several original PARTICLE_*.TGA atlases store emissive RGB on an
        // opaque black background. Treat brightness as additional coverage so
        // those atlases use their intended black-key particle composition,
        // while preserving authored alpha on textures which have it.
        color.a *= max(color.r, max(color.g, color.b));
    }
    if (color.a < (is_liquid || is_particle || is_mixed_light ? (1.0f / 255.0f) : alpha_cutoff))
        discard;

    bool is_unlit = !is_liquid && (input.flags & 0x80000000u) != 0;
    float emission = is_mixed_light ? mixed_light_emission(color.rgb) : 0.0f;
    float3 lighting = surface_lighting(input.position.xy);
    color.rgb *= is_unlit ? 1.0f : lerp(lighting, 1.0f, emission);

    pixel_output output;
    output.color = color;
    output.depth = input.depth;
    return output;
}

pixel_output ps_hdr(vertex_output input)
{
    float4 tex = sample_sprite_texture(static_texture, input);

    bool is_liquid = (input.flags & 1) != 0;
    if (is_liquid)
    {
        tex.a *= liquid_corner_alpha(input.tex_coord, input.corner_alpha);
        if (tex.a < 1.0f / 255.0f)
            discard;
    }
    else
    {
        bool is_particle = (input.flags & 0x40000000u) != 0;
        bool is_mixed_light = (input.flags & 0x20000000u) != 0;
        if (is_particle)
            tex.a *= max(tex.r, max(tex.g, tex.b));
        if (tex.a < (is_particle || is_mixed_light ? (1.0f / 255.0f) : alpha_cutoff))
            discard;
    }

    bool is_unlit = !is_liquid && (input.flags & 0x80000000u) != 0;
    bool is_mixed_light = !is_liquid && (input.flags & 0x20000000u) != 0;
    float emission = is_mixed_light ? mixed_light_emission(tex.rgb) : 0.0f;
    float3 lighting = surface_lighting(input.position.xy);
    tex.rgb *= is_unlit ? 1.0f : lerp(lighting, 1.0f, emission);
    float nits = is_unlit
        ? unlit_white_nits
        : lerp(scene_paper_white, unlit_white_nits, emission);

    pixel_output output;
    // Fixed-function blending happens in the PQ-encoded back buffer. Premultiply
    // the encoded value so the blend unit applies coverage exactly once; encoding
    // coverage as luminance first makes translucent edges too bright, especially
    // against night terrain.
    output.color = float4(SdrTextureToHdr10(tex.rgb, nits) * tex.a, tex.a);
    output.depth = input.depth;
    return output;
}
