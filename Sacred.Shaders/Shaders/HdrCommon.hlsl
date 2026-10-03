// HDR is composed in linear scRGB (Rec.709 primaries), where 1.0 is 80 nits.
// The presentation shader converts the completed frame to Rec.2020/PQ HDR10.
float3 SrgbToLinear(float3 color)
{
    return pow(max(color, 0.0f), 2.2f);
}

float3 SdrTextureToScrgb(float3 color, float white_nits)
{
    return SrgbToLinear(color) * (white_nits / 80.0f);
}

float3 SdrLitTextureToScrgb(
    float3 base_color,
    float3 ambient,
    float3 diffuse,
    float3 specular,
    float paper_white_nits,
    float diffuse_white_nits,
    float specular_white_nits)
{
    // Retain Sacred's lighting response in authored texture space, then extend
    // its range. No exposure adaptation or color grading is applied.
    float3 lit_color =
        base_color * (ambient + diffuse * (diffuse_white_nits / paper_white_nits)) +
        specular * (specular_white_nits / paper_white_nits);
    return SdrTextureToScrgb(lit_color, paper_white_nits);
}

float3 SdrTextureToPremultipliedScrgb(float3 color, float alpha, float white_nits)
{
    return SdrTextureToScrgb(color, white_nits) * alpha;
}

float3 SdrParticleToScrgb(float3 color, float coverage, float white_nits)
{
    return SdrTextureToPremultipliedScrgb(color, coverage, white_nits);
}

// Sprite composition happens in Sacred's authored RGB space in an FP16
// target. Decode the completed blend, not each individual texture/fade/tint.
struct hdr_particle_output
{
    float4 color : SV_Target0;
};

// Preserve the authored transfer below reference white. Above white, extend
// its tangent line: accumulated over-white RGB keeps increasing without being
// raised to gamma again. This is a fixed, reversible extension, with no exposure
// adaptation, background estimate, channel attenuation, or clipping ceiling.
float3 HdrArtToLinear(float3 color)
{
    float3 positive = max(color, 0.0f);
    return float3(
        positive.r <= 1.0f ? pow(positive.r, 2.2f) : 1.0f + (positive.r - 1.0f) * 2.2f,
        positive.g <= 1.0f ? pow(positive.g, 2.2f) : 1.0f + (positive.g - 1.0f) * 2.2f,
        positive.b <= 1.0f ? pow(positive.b, 2.2f) : 1.0f + (positive.b - 1.0f) * 2.2f);
}

float3 LinearToHdrArt(float3 color)
{
    float3 positive = max(color, 0.0f);
    return float3(
        positive.r <= 1.0f ? pow(positive.r, 1.0f / 2.2f) : 1.0f + (positive.r - 1.0f) / 2.2f,
        positive.g <= 1.0f ? pow(positive.g, 1.0f / 2.2f) : 1.0f + (positive.g - 1.0f) / 2.2f,
        positive.b <= 1.0f ? pow(positive.b, 1.0f / 2.2f) : 1.0f + (positive.b - 1.0f) / 2.2f);
}

float3 HdrArtColor(float3 color, float white_nits, float scene_white_nits)
{
    return color * LinearToHdrArt((white_nits / scene_white_nits).xxx);
}

float4 HdrEquipmentParticle(float3 texture_color, float3 tint, float coverage, float white_nits, float scene_white_nits)
{
    return float4(HdrArtColor(texture_color * tint, white_nits, scene_white_nits) * coverage, 0.0f);
}

hdr_particle_output HdrParticle(
    float3 texture_color, float3 tint, float source_scale, float coverage, float white_nits, float scene_white_nits, bool additive)
{
    hdr_particle_output output;
    output.color = float4(HdrArtColor(texture_color * tint, white_nits, scene_white_nits) * source_scale,
        additive ? 0.0f : coverage);
    return output;
}
