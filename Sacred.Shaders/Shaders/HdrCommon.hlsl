float3 SrgbToLinear(float3 color)
{
    return pow(color, 2.2f);
}

float3 Linear709To2020(float3 color)
{
    return float3(
        dot(color, float3(0.6274040f, 0.3292820f, 0.0433136f)),
        dot(color, float3(0.0690970f, 0.9195400f, 0.0113612f)),
        dot(color, float3(0.0163916f, 0.0880132f, 0.8955950f))
    );
}

float LinearNitsToPQ(float nits)
{
    const float m1 = 2610.0f / 16384.0f;
    const float m2 = 2523.0f / 32.0f;
    const float c1 = 3424.0f / 4096.0f;
    const float c2 = 2413.0f / 128.0f;
    const float c3 = 2392.0f / 128.0f;

    float normalized_nits = nits / 10000.0f;
    float p = pow(normalized_nits, m1);
    return pow((c1 + c2 * p) / (1.0f + c3 * p), m2);
}

float3 LinearNitsToPQ(float3 nits)
{
    return float3(
        LinearNitsToPQ(nits.r),
        LinearNitsToPQ(nits.g),
        LinearNitsToPQ(nits.b)
    );
}

float3 SdrTextureToHdr10(float3 color, float paper_white_nits)
{
    float3 nits709 = SrgbToLinear(color) * paper_white_nits;
    return LinearNitsToPQ(Linear709To2020(nits709));
}

float3 SdrLitTextureToHdr10(
    float3 base_color,
    float3 ambient,
    float3 diffuse,
    float3 specular,
    float paper_white_nits,
    float diffuse_white_nits,
    float specular_white_nits)
{
    // Sacred's SDR renderer applies its lighting to the encoded texture color.
    // Preserve that response before converting to PQ; applying small night-light
    // factors after decoding to linear makes dark models substantially brighter.
    float diffuse_scale = diffuse_white_nits / paper_white_nits;
    float specular_scale = specular_white_nits / paper_white_nits;
    float3 lit_color =
        base_color * (ambient + diffuse * diffuse_scale) +
        specular * specular_scale;
    return SdrTextureToHdr10(lit_color, paper_white_nits);
}

float3 Linear709NitsToHdr10(float3 nits709)
{
    return LinearNitsToPQ(Linear709To2020(max(nits709, 0.0f)));
}

float3 SdrTextureToPremultipliedHdr10(float3 color, float alpha, float paper_white_nits)
{
    // Apply coverage after decoding sRGB but before PQ encoding. Multiplying an
    // already PQ-encoded value would crush partially transparent highlights.
    float3 premultiplied_nits709 = SrgbToLinear(color) * paper_white_nits * alpha;
    return Linear709NitsToHdr10(premultiplied_nits709);
}

float3 SdrParticleToHdr10(float3 color, float coverage, float white_nits)
{
    // Keep coverage outside the transfer function when the fixed-function blend
    // unit consumes premultiplied PQ values.
    return SdrTextureToHdr10(color, white_nits) * coverage;
}

float3 SdrParticleToHdr10Screen(
    float3 color,
    float coverage,
    float background_white_nits,
    float particle_white_nits)
{
    // Solve S + B * (1-S) = C for S, using paper white as the representative
    // background and paper white plus the emissive particle as the desired result.
    // Feeding a PQ-encoded particle directly to screen blending interprets its PQ
    // code as coverage and drives even soft particles toward the display maximum.
    float3 background_pq = LinearNitsToPQ(background_white_nits.xxx);
    float3 particle_nits = Linear709To2020(SrgbToLinear(color)) * particle_white_nits;
    float3 combined_pq = LinearNitsToPQ(background_white_nits.xxx + particle_nits);
    float3 screen_contribution = (combined_pq - background_pq) /(1.0f - background_pq);
    return screen_contribution * coverage;
}
