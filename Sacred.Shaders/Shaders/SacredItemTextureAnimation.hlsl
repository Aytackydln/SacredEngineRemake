static const float native_item_transform_encoding_offset = 16.0f;

// Gold cGranny::renderComplex (0x406510): integer milliseconds, low 16 bits.
float2 native_item_tex_coord(float2 uv, float seconds, uint selector)
{
    float ticks = fmod(floor(max(seconds, 0.0f) * 1000.0f), 65536.0f);
    if (selector == 1) return uv + ticks / 16384.0f;
    if (selector == 2) return uv + float2(0.0f, ticks / 2048.0f);
    if (selector == 3) return uv + float2(ticks / 2048.0f, 0.0f);
    if (selector == 4) return uv + float2(ticks / 1024.0f, 0.0f);
    if (selector == 6) return uv + float2(0.0f, ticks / 1024.0f);
    if (selector == 9)
    {
        float phase = fmod(ticks, 2048.0f);
        // Native leaves the identity matrix intact in the second half-period.
        if (phase >= 1024.0f) return uv;
        float v = phase < 512.0f ? phase / 512.0f : (phase - 512.0f) / 512.0f;
        return uv + float2(phase / 128.0f, pow(v, 1.3f));
    }
    return uv;
}


float3 native_secondary_item_glow(float2 uv, float seconds, float descriptor_slot, SamplerState texture_sampler)
{
    if (descriptor_slot < 0.0f) return 0.0f;
    Texture2D glow = ResourceDescriptorHeap[(uint)descriptor_slot];
    float ticks = fmod(floor(max(seconds, 0.0f) * 1000.0f), 65536.0f);
    return glow.Sample(texture_sampler, uv + ticks / 32767.0f).rgb;
}
