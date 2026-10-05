// Sampling and player-occlusion behavior shared by lit and unlit static sprites.

uint portal_texture_index(uint seed, uint cycle, uint count)
{
    // Gold chooses rand()%16 on each five-second cycle. A stable per-script seed
    // provides the same irregular selection without CPU texture changes every frame.
    uint value = seed + cycle * 0x9E3779B9u;
    value = (value ^ (value >> 16u)) * 0x7FEB352Du;
    value = (value ^ (value >> 15u)) * 0x846CA68Bu;
    return (value ^ (value >> 16u)) % count;
}

float3 portal_surface_vertex(float2 vertex, float phase)
{
    // Gold computes colour at its 20x34 vertices, then Gouraud-interpolates it.
    // Evaluating the normal per pixel instead produces sharp, dark concentric rings.
    float2 grid = vertex - float2(9.5f, 17.0f);
    float radius = length(grid);
    float2 end_center = float2(0.0f, clamp(grid.y, -7.0f, 7.0f));
    float edge_distance = length(grid - end_center);
    float alpha_bucket = clamp(floor((10.0f - edge_distance) * 3.2f), 0.0f, 15.0f);
    float wave = sin(radius - animation_time * 5.0f);
    float2 slope = radius > 0.0001f ? grid * (-wave / radius) : 0.0f.xx;
    float3 normal = normalize(float3(slope.x, -1.0f, slope.y));
    float shade = floor(clamp((dot(normal, float3(2.0f, -2.0f, 1.0f) / 3.0f) + 1.0f) * 127.9f, 0.0f, 255.0f)) / 255.0f;
    float first_bucket = clamp(floor((5.0f - phase) * 15.9f), 0.0f, 15.0f);
    float second_bucket = clamp(floor((phase - 3.0f) * 15.9f), 0.0f, 15.0f);
    float first_alpha = floor(alpha_bucket * first_bucket * (17.0f / 15.0f)) / 255.0f;
    float second_alpha = floor(alpha_bucket * second_bucket * (17.0f / 15.0f)) / 255.0f;
    return float3(shade, first_alpha, second_alpha);
}

float3 portal_surface_colour(float2 uv, float phase)
{
    float2 grid = float2(uv.x * 19.0f, (1.0f - uv.y) * 33.0f);
    float2 cell = min(floor(grid), float2(18.0f, 32.0f));
    float2 fraction = grid - cell;
    float3 right = portal_surface_vertex(cell + float2(1.0f, 0.0f), phase);
    float3 above = portal_surface_vertex(cell + float2(0.0f, 1.0f), phase);
    if (fraction.x + fraction.y <= 1.0f)
        return portal_surface_vertex(cell, phase) * (1.0f - fraction.x - fraction.y) +
            right * fraction.x + above * fraction.y;
    return portal_surface_vertex(cell + 1.0f.xx, phase) * (fraction.x + fraction.y - 1.0f) +
        right * (1.0f - fraction.y) + above * (1.0f - fraction.x);
}

float4 sample_portal_surface(Texture2D texture_to_sample, vertex_output input)
{
    uint atlas_width;
    uint atlas_height;
    texture_to_sample.GetDimensions(atlas_width, atlas_height);
    uint columns = max(1u, input.atlas_columns);
    uint rows = max(1u, input.atlas_rows);
    float2 frame_size = float2(atlas_width / columns, atlas_height / rows);
    uint cycle = (uint)(animation_time / 5.0f);
    uint first = portal_texture_index(input.texture_variant, cycle, input.frame_count);
    uint second = portal_texture_index(input.texture_variant, cycle + 1u, input.frame_count);
    float2 local_uv = float2(input.tex_coord.x * (19.0f / 33.0f), input.tex_coord.y);
    float2 pixel_uv = 0.5f + local_uv * (frame_size - 1.0f);
    float2 first_uv = (float2(first % columns, first / columns) * frame_size + pixel_uv) / float2(atlas_width, atlas_height);
    float2 second_uv = (float2(second % columns, second / columns) * frame_size + pixel_uv) / float2(atlas_width, atlas_height);
    float phase = animation_time % 5.0f;
    float3 vertex_colour = portal_surface_colour(input.tex_coord, phase);
    float first_alpha = vertex_colour.y;
    float second_alpha = vertex_colour.z;
    float coverage = second_alpha + first_alpha * (1.0f - second_alpha);
    float3 premultiplied = texture_to_sample.Sample(sampler0, second_uv).rgb * second_alpha +
        texture_to_sample.Sample(sampler0, first_uv).rgb * first_alpha * (1.0f - second_alpha);
    return float4(premultiplied / max(coverage, 0.0001f) * vertex_colour.x, coverage);
}

float4 sample_static_texture(Texture2D texture_to_sample, vertex_output input)
{
    if ((input.particle_sprite & 64u) != 0)
        return sample_portal_surface(texture_to_sample, input);
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
    Texture2D static_texture = ResourceDescriptorHeap[NonUniformResourceIndex(input.texture_index)];
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
