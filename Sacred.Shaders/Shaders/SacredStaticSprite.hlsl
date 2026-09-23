// Lit static-sprite pixel shaders.
#pragma vertex vs_main
#pragma fragment ps_sdr

float3 surface_lighting(float2 pixel_position)
{
    float lighting = surface_light_map.Sample(sampler0, pixel_position / viewport_size);
    return min(ambient_colour + lighting, 1.0f);
}

float mixed_light_emission(float3 colour)
{
    // Class-9 mixed sprites can contain blue magical emitters or orange fire.
    // Preserve only those authored emitter pixels (plus white-hot glints), while
    // their fixture art continues to receive ambient and local surface light.
    // Match MixedLightAppearanceCache: muted blue cloth and painted signs are
    // scene-lit; only the saturated blue used for magical emitters is unlit.
    float blue_dominance = saturate((colour.b - colour.r - 0.12f) * 6.0f) *
        saturate((colour.b - colour.g - 0.12f) * 8.0f);
    float warm_dominance = saturate((colour.r - colour.b - 0.10f) * 4.0f) *
        saturate((max(colour.r, colour.g) - 0.55f) * 4.0f);
    float white_glint = saturate((max(colour.r, max(colour.g, colour.b)) - 0.80f) * 5.0f);
    return max(max(blue_dominance, warm_dominance), white_glint);
}

pixel_output render_static_sdr(vertex_output input, float opacity)
{
    float4 color = sample_static_pixel(input, opacity);

    float emission = input.mixed_light_emitter != 0 ? mixed_light_emission(color.rgb) : 0.0f;
    float3 lighting = surface_lighting(input.position.xy);
    color.rgb *= lerp(lighting, 1.0f, emission);

    pixel_output output;
    output.color = float4(color.rgb * color.a, color.a);
    output.depth = input.depth;
    return output;
}

pixel_output ps_sdr(vertex_output input)
{
    return render_static_sdr(input, 1.0f);
}

pixel_output ps_transparent_sdr(vertex_output input)
{
    return render_static_sdr(input, player_occluder_opacity(input));
}

pixel_output render_static_hdr(vertex_output input, float opacity)
{
    float4 tex = sample_static_pixel(input, opacity);
    tex.rgb *= surface_lighting(input.position.xy);

    pixel_output output;
    output.color = float4(SdrTextureToHdr10(tex.rgb, scene_paper_white) * tex.a, tex.a);
    output.depth = input.depth;
    return output;
}

pixel_output ps_hdr(vertex_output input)
{
    return render_static_hdr(input, 1.0f);
}

pixel_output ps_transparent_hdr(vertex_output input)
{
    return render_static_hdr(input, player_occluder_opacity(input));
}
