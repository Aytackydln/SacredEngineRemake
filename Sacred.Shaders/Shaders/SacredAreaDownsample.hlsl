cbuffer Dimensions : register(b0)
{
    uint2 SourceSize;
    uint2 OutputSize;
};
Texture2D<float4> Source : register(t0);

float4 vs_main(uint id : SV_VertexID) : SV_POSITION
{
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

// Match the CPU filter's round-to-even without float division near half ties.
uint DivideRounded(uint numerator, uint denominator)
{
    uint quotient = numerator / denominator;
    uint remainder = numerator % denominator;
    return quotient + (remainder > denominator - remainder ||
        (remainder == denominator - remainder && (quotient & 1)));
}

float4 ps_main(float4 position : SV_POSITION) : SV_TARGET
{
    uint2 destination = uint2(position.xy);
    uint2 factor = SourceSize / OutputSize;
    // A uint channel accumulator can hold 65535 fully opaque source samples.
    if (all(SourceSize % OutputSize == 0) && factor.x * factor.y <= 65535)
    {
        uint4 sum = 0;
        uint2 origin = destination * factor;
        [loop] for (uint y = 0; y < factor.y; y++)
        [loop] for (uint x = 0; x < factor.x; x++)
        {
            uint4 sample = uint4(round(Source.Load(int3(origin + uint2(x, y), 0)) * 255));
            sum.rgb += sample.rgb * sample.a;
            sum.a += sample.a;
        }
        if (sum.a == 0) return 0;
        uint4 result = uint4(DivideRounded(sum.r, sum.a), DivideRounded(sum.g, sum.a),
            DivideRounded(sum.b, sum.a), DivideRounded(sum.a, factor.x * factor.y));
        return float4(result) / 255;
    }

    // Fractional coverage also supports rectangular packed minimap dimensions.
    float2 scale = float2(SourceSize) / float2(OutputSize);
    float2 first = destination * scale;
    float2 last = min((destination + 1) * scale, float2(SourceSize));
    float4 sum = 0;
    [loop] for (uint sy = uint(first.y); sy < uint(ceil(last.y)); sy++)
    [loop] for (uint sx = uint(first.x); sx < uint(ceil(last.x)); sx++)
    {
        float weight = (min(last.x, sx + 1) - max(first.x, sx)) *
            (min(last.y, sy + 1) - max(first.y, sy));
        float4 sample = round(Source.Load(int3(sx, sy, 0)) * 255);
        float coverage = sample.a * weight;
        sum += float4(sample.rgb * coverage, coverage);
    }
    if (sum.a == 0) return 0;
    return round(float4(sum.rgb / sum.a, sum.a / (scale.x * scale.y))) / 255;
}
