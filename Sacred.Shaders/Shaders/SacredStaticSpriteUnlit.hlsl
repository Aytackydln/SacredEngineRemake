// Unlit static-sprite pixel shaders.
#pragma vertex vs_main
#pragma fragment ps_unlit_sdr

pixel_output render_unlit_sdr(vertex_output input, float opacity)
{
    float4 color = sample_static_pixel(input, opacity);
    pixel_output output;
    float coverage = color.a;
    float source_scale = (input.particle_sprite & 4) != 0 ? opacity : coverage;
    // With the premultiplied ONE / INV_SRC_ALPHA pipeline, zero output alpha
    // preserves the destination while adding the particle's RGB contribution.
    output.color = float4(color.rgb * source_scale,
        (input.particle_sprite & 2) != 0 ? 0.0f : coverage);
    output.depth = input.depth;
    return output;
}

pixel_output ps_unlit_sdr(vertex_output input)
{
    return render_unlit_sdr(input, 1.0f);
}

pixel_output ps_transparent_unlit_sdr(vertex_output input)
{
    return render_unlit_sdr(input, player_occluder_opacity(input));
}

pixel_output render_unlit_hdr_rgb(vertex_output input, float opacity)
{
    float3 texture_color = sample_static_texture(static_texture, input).rgb;
    float coverage = input.corner_alpha.a * opacity;
    if (coverage < (1.0f / 255.0f))
        discard;

    pixel_output output;
    float source_scale = (input.particle_sprite & 4) != 0 ? opacity : coverage;
    output.color = float4(SdrParticleToHdr10(
        texture_color * input.corner_alpha.rgb,
        source_scale,
        scene_paper_white),
        (input.particle_sprite & 2) != 0 ? 0.0f : coverage);
    output.depth = input.depth;
    return output;
}

pixel_output render_unlit_hdr_argb(vertex_output input, float opacity)
{
    float4 sampled = sample_static_texture(static_texture, input);
    float coverage = sampled.a * input.corner_alpha.a * opacity;
    if (coverage < (1.0f / 255.0f))
        discard;

    pixel_output output;
    float source_scale = (input.particle_sprite & 4) != 0
        ? sampled.a * opacity
        : coverage;
    output.color = float4(SdrParticleToHdr10(
        sampled.rgb * input.corner_alpha.rgb,
        source_scale,
        scene_paper_white),
        (input.particle_sprite & 2) != 0 ? 0.0f : coverage);
    output.depth = input.depth;
    return output;
}

pixel_output render_unlit_hdr_alpha_mask(vertex_output input, float opacity)
{
    float mask = sample_static_texture(static_texture, input).a;
    float coverage = mask * input.corner_alpha.a * opacity;
    if (coverage < (1.0f / 255.0f))
        discard;

    pixel_output output;
    float source_scale = (input.particle_sprite & 4) != 0
        ? mask * opacity
        : coverage;
    output.color = float4(SdrParticleToHdr10(
        input.corner_alpha.rgb,
        source_scale,
        scene_paper_white),
        (input.particle_sprite & 2) != 0 ? 0.0f : coverage);
    output.depth = input.depth;
    return output;
}

pixel_output ps_transparent_unlit_hdr_rgb(vertex_output input)
{
    return render_unlit_hdr_rgb(input, player_occluder_opacity(input));
}

pixel_output ps_transparent_unlit_hdr_argb(vertex_output input)
{
    return render_unlit_hdr_argb(input, player_occluder_opacity(input));
}

pixel_output ps_transparent_unlit_hdr_alpha_mask(vertex_output input)
{
    return render_unlit_hdr_alpha_mask(input, player_occluder_opacity(input));
}

// Kept for non-particle unlit sprites and as the generic HDR entry point.
pixel_output render_unlit_hdr(vertex_output input, float opacity)
{
    float4 tex = sample_static_pixel(input, opacity);
    pixel_output output;
    float coverage = tex.a;
    float white_nits = input.particle_sprite != 0 ? scene_paper_white : unlit_white_nits;
    float source_scale = (input.particle_sprite & 4) != 0 ? opacity : coverage;
    output.color = float4(SdrParticleToHdr10(tex.rgb, source_scale, white_nits),
        (input.particle_sprite & 2) != 0 ? 0.0f : coverage);
    output.depth = input.depth;
    return output;
}

pixel_output ps_unlit_hdr(vertex_output input)
{
    return render_unlit_hdr(input, 1.0f);
}

pixel_output ps_transparent_unlit_hdr(vertex_output input)
{
    return render_unlit_hdr(input, player_occluder_opacity(input));
}
