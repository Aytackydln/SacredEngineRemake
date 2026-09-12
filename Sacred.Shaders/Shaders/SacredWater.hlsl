// Water sprite pixel shaders.
#pragma vertex vs_main
#pragma fragment ps_water_sdr

float3 water_surface_lighting(float2 pixel_position)
{
    float lighting = surface_light_map.Sample(sampler0, pixel_position / viewport_size);
    return min(ambient_colour + lighting, 1.0f);
}

float liquid_corner_alpha(float2 uv, float4 corner_alpha)
{
    float2 liquid_position = uv * 2.0f - 1.0f;
    if (abs(liquid_position.x) + abs(liquid_position.y) > 1.0f)
        discard;

    // cWorldView0::renderWater submits a four-vertex triangle strip.  Its
    // diagonal runs from the top vertex to the bottom vertex, matching the
    // terrain diamond (left, top, bottom) then (top, right, bottom).  Do not
    // introduce a synthetic centre vertex: it changes the authored shoreline
    // alpha surface into four visible diamond wedges.
    float interpolated_alpha;
    if (liquid_position.x <= 0.0f)
    {
        float left_weight = -liquid_position.x;
        float top_weight = (1.0f + liquid_position.x - liquid_position.y) * 0.5f;
        float bottom_weight = (1.0f + liquid_position.x + liquid_position.y) * 0.5f;
        interpolated_alpha = left_weight * corner_alpha.x +
                             top_weight * corner_alpha.y +
                             bottom_weight * corner_alpha.w;
    }
    else
    {
        float right_weight = liquid_position.x;
        float top_weight = (1.0f - liquid_position.x - liquid_position.y) * 0.5f;
        float bottom_weight = (1.0f - liquid_position.x + liquid_position.y) * 0.5f;
        interpolated_alpha = right_weight * corner_alpha.z +
                             top_weight * corner_alpha.y +
                             bottom_weight * corner_alpha.w;
    }

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

float4 sample_water_texture(Texture2D texture_to_sample, vertex_output input)
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
    uint liquid_phase = (elapsed_milliseconds >> 1) & 1023;
    uint frame_index = min(
        (liquid_phase * input.frame_count) >> 10,
        input.frame_count - 1);
    uint frame_column = frame_index % atlas_columns;
    uint frame_row = frame_index / atlas_columns;

    // Water frames are authored as top-down 128x128 textures spanning a
    // four-by-four block of terrain cells. Preserve their spatial detail by
    // sampling the appropriate cell region rather than repeating the entire
    // frame on every projected diamond.
    float2 cell_uv = float2(
        input.tex_coord.x + input.tex_coord.y - 0.5f,
        input.tex_coord.y - input.tex_coord.x + 0.5f);
    float2 block_cell = float2(
        input.texture_variant & 3,
        (input.texture_variant >> 2) & 3);
    float2 frame_uv = (block_cell + cell_uv) * 0.25f;
    return sample_repeating_frame(
        texture_to_sample,
        frame_column,
        frame_row,
        frame_width,
        frame_height,
        frame_uv);
}

pixel_output ps_water_sdr(vertex_output input)
{
    float4 color = sample_water_texture(static_texture, input);
    color.a *= liquid_corner_alpha(input.tex_coord, input.corner_alpha);
    if (color.a < 1.0f / 255.0f)
        discard;
    color.rgb *= water_surface_lighting(input.position.xy);

    pixel_output output;
    output.color = color;
    output.depth = input.depth;
    return output;
}

pixel_output ps_water_hdr(vertex_output input)
{
    float4 tex = sample_water_texture(static_texture, input);
    tex.a *= liquid_corner_alpha(input.tex_coord, input.corner_alpha);
    if (tex.a < 1.0f / 255.0f)
        discard;
    tex.rgb *= water_surface_lighting(input.position.xy);

    pixel_output output;
    output.color = float4(SdrTextureToHdr10(tex.rgb, scene_paper_white) * tex.a, tex.a);
    output.depth = input.depth;
    return output;
}
