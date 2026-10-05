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

float hdr_unlit_color_multiplier(vertex_output input)
{
    // Portal layers are one native composited image (including minimap colours).
    // Amplifying each layer before blending washes out the surface and additive
    // halo. Keep their authored RGB; frame-white still supplies HDR brightness.
    if ((input.particle_sprite & 112u) != 0) return 1.0f;
    return input.particle_sprite != 0 ? particle_color_multiplier : unlit_white_nits;
}

particle_pixel_output render_unlit_hdr_rgb(vertex_output input, float opacity)
{
    Texture2D static_texture = ResourceDescriptorHeap[NonUniformResourceIndex(input.texture_index)];
    float3 texture_color = sample_static_texture(static_texture, input).rgb;
    float coverage = input.corner_alpha.a * opacity;
    if (coverage < (1.0f / 255.0f))
        discard;

    float source_scale = (input.particle_sprite & 4) != 0 ? opacity : coverage;
    return ComposeParticle(
        texture_color, input.corner_alpha.rgb * hdr_unlit_color_multiplier(input), source_scale, coverage, (input.particle_sprite & 2) != 0);
}

particle_pixel_output render_unlit_hdr_argb(vertex_output input, float opacity)
{
    Texture2D static_texture = ResourceDescriptorHeap[NonUniformResourceIndex(input.texture_index)];
    float4 sampled = sample_static_texture(static_texture, input);
    float coverage = sampled.a * input.corner_alpha.a * opacity;
    if (coverage < (1.0f / 255.0f))
        discard;

    float source_scale = (input.particle_sprite & 4) != 0
        ? sampled.a * opacity
        : coverage;
    return ComposeParticle(
        sampled.rgb, input.corner_alpha.rgb * hdr_unlit_color_multiplier(input), source_scale, coverage, (input.particle_sprite & 2) != 0);
}

particle_pixel_output render_unlit_hdr_alpha_mask(vertex_output input, float opacity)
{
    Texture2D static_texture = ResourceDescriptorHeap[NonUniformResourceIndex(input.texture_index)];
    float mask = sample_static_texture(static_texture, input).a;
    float coverage = mask * input.corner_alpha.a * opacity;
    if (coverage < (1.0f / 255.0f))
        discard;

    float source_scale = (input.particle_sprite & 4) != 0
        ? mask * opacity
        : coverage;
    return ComposeParticle(1.0f.xxx, input.corner_alpha.rgb * hdr_unlit_color_multiplier(input), source_scale, coverage, (input.particle_sprite & 2) != 0);
}

[earlydepthstencil]
particle_pixel_output ps_transparent_unlit_hdr_rgb(vertex_output input)
{
    return render_unlit_hdr_rgb(input, player_occluder_opacity(input));
}

[earlydepthstencil]
particle_pixel_output ps_transparent_unlit_hdr_argb(vertex_output input)
{
    return render_unlit_hdr_argb(input, player_occluder_opacity(input));
}

[earlydepthstencil]
particle_pixel_output ps_transparent_unlit_hdr_alpha_mask(vertex_output input)
{
    return render_unlit_hdr_alpha_mask(input, player_occluder_opacity(input));
}

// Kept for non-particle unlit sprites and as the generic HDR entry point.
pixel_output render_unlit_hdr(vertex_output input, float opacity)
{
    float4 tex = sample_static_pixel(input, opacity);
    pixel_output output;
    float coverage = tex.a;
    float source_scale = (input.particle_sprite & 4) != 0 ? opacity : coverage;
    output.color = float4(tex.rgb * source_scale * hdr_unlit_color_multiplier(input),
        (input.particle_sprite & 2) != 0 ? 0.0f : coverage);
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

pixel_output ps_unlit_sdr_opaque(vertex_output input)
{
    float4 tex = sample_static_pixel(input, 1.0f);
    pixel_output output;
    output.color = float4(tex.rgb, 1.0f);
    return output;
}

pixel_output ps_unlit_hdr_opaque(vertex_output input)
{
    float4 tex = sample_static_pixel(input, 1.0f);
    pixel_output output;
    output.color = float4(tex.rgb * unlit_white_nits, 1.0f);
    return output;
}
