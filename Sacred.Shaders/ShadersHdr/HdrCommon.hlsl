float3 SrgbToLinear(float3 color)
{
    return pow(saturate(color), 2.2f);
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

    float normalized_nits = saturate(nits / 10000.0f);
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

float3 Linear709NitsToHdr10(float3 nits709)
{
    return LinearNitsToPQ(Linear709To2020(max(nits709, 0.0f)));
}
