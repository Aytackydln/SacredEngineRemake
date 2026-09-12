// Sampling and player-occlusion behavior shared by lit and unlit static sprites.

float4 sample_static_texture(Texture2D texture_to_sample, vertex_output input)
{
    if (input.frame_count <= 1 || (input.particle_sprite == 0 && input.animation_period_seconds <= 0.0f))
        return texture_to_sample.Sample(sampler0, input.tex_coord);

    uint atlas_width;
    uint atlas_height;
    texture_to_sample.GetDimensions(atlas_width, atlas_height);
    uint atlas_columns = max(1, input.atlas_columns);
    uint atlas_rows = max(1, input.atlas_rows);
    uint frame_width = max(1, atlas_width / atlas_columns);
    uint frame_height = max(1, atlas_height / atlas_rows);
    uint elapsed_milliseconds = (uint)(animation_time * 1000.0f);
    uint animation_period_milliseconds = max(
        1,
        (uint)round(input.animation_period_seconds * 1000.0f));
    uint frame_index = (elapsed_milliseconds % animation_period_milliseconds) *
        input.frame_count / animation_period_milliseconds;
    if (input.particle_sprite != 0) frame_index = input.texture_variant;
    frame_index = min(frame_index, input.frame_count - 1);
    uint frame_column = frame_index % atlas_columns;
    uint frame_row = frame_index / atlas_columns;
    float2 frame_uv = input.tex_coord;
    if (input.transpose_texture != 0)
        frame_uv = input.tex_coord.yx;
    float2 atlas_uv = float2(
        (frame_column * frame_width + 0.5f + frame_uv.x * (frame_width - 1.0f)) / atlas_width,
        (frame_row * frame_height + 0.5f + frame_uv.y * (frame_height - 1.0f)) / atlas_height);
    return texture_to_sample.Sample(sampler0, atlas_uv);
}

float player_occluder_opacity(vertex_output input)
{
    if (input.player_occlusion_fade == 0)
        return 1.0f;

    // Smaller painter-depth values are closer to the camera. Fade only foreground
    // pixels using the player-occlusion texture generated for this frame.
    if (input.depth >= player_scene_depth)
        return 1.0f;

    float reveal = player_occlusion_map.Sample(
        sampler0,
        input.position.xy / viewport_size);
    return lerp(1.0f, occluder_opacity, reveal);
}

float4 sample_static_pixel(vertex_output input, float opacity)
{
    float4 color = sample_static_texture(static_texture, input);

    if (color.a == 0)
    {
        discard;
    }

    bool is_particle = input.particle_sprite != 0;
    bool is_mixed_light = input.mixed_light_emitter != 0;
    if (is_particle)
    {
        // Particle corner_alpha carries the native RGBA fade-table entry.
        color *= input.corner_alpha;
    }
    if (color.a < (is_particle || is_mixed_light ? (1.0f / 255.0f) : alpha_cutoff))
        discard;
    color.a *= opacity;
    return color;
}
