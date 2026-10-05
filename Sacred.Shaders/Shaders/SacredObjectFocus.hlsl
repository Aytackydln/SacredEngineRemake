// Bit-like offset in texture mode; no extra root constants or render pass are needed.
float focused_texture_mode(float packed_mode)
{
    return packed_mode >= 8.0f ? packed_mode - 8.0f : packed_mode;
}

float world_focus_light_scale(float packed_mode, float elapsed_seconds)
{
    if (packed_mode < 8.0f) return 1.0f;
    // Demo renderObjects 0x5A0754..0x5A07BD. Normal prop intensity is 0.8.
    float milliseconds = floor(elapsed_seconds * 1000.0f);
    float intensity = abs(0.8f - fmod(milliseconds, 400.0f) * 0.004f) + 0.2f;
    return intensity / 0.8f;
}
