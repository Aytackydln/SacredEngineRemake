// Both output modes blend Sacred's authored RGB. Only HDR presentation decodes
// the completed frame; these helpers apply native particle tint and coverage.
struct particle_pixel_output
{
    float4 color : SV_Target0;
};

particle_pixel_output ComposeParticle(
    float3 texture_color, float3 tint, float source_scale, float coverage, bool additive)
{
    particle_pixel_output output;
    // ONE / INV_SRC_ALPHA: zero alpha adds RGB without attenuating the destination.
    // Source RGB energy and destination coverage remain independent.
    output.color = float4(texture_color * tint * source_scale, additive ? 0.0f : coverage);
    return output;
}

float4 ComposeAdditiveParticle(float3 texture_color, float3 tint, float coverage)
{
    return ComposeParticle(texture_color, tint, coverage, coverage, true).color;
}
