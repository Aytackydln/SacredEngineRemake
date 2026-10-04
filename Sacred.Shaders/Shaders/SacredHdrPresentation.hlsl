// Sacred blends the entire floating-point frame in authored RGB, as its SDR framebuffer
// did. Decode once, after all particles, transparency, scaling and UI.
cbuffer output_constants : register(b0)
{
    float frame_white_nits;
};
Texture2D<float4> frame_color : register(t0);

float4 vs_main(uint vertex : SV_VertexID) : SV_Position
{
    return float4(vertex == 2 ? 3.0f : -1.0f, vertex == 1 ? 3.0f : -1.0f, 0.0f, 1.0f);
}

float4 ps_main(float4 position : SV_Position) : SV_Target
{
    float3 authored = frame_color.Load(int3(position.xy, 0)).rgb;
    // Below white preserve the original gamma. Above white continue its tangent:
    // accumulated RGB is neither clipped nor raised to gamma again.
    float3 linear709 = pow(saturate(authored), 2.2f) + max(authored - 1.0f, 0.0f) * 2.2f;
    float3 linear2020 = float3(
        dot(linear709, float3(0.6274040f, 0.3292820f, 0.0433136f)),
        dot(linear709, float3(0.0690970f, 0.9195400f, 0.0113612f)),
        dot(linear709, float3(0.0163916f, 0.0880132f, 0.8955950f)));
    float3 p = pow(saturate(linear2020 * (frame_white_nits / 10000.0f)), 2610.0f / 16384.0f);
    float3 pq = pow((3424.0f / 4096.0f + (2413.0f / 128.0f) * p) /
        (1.0f + (2392.0f / 128.0f) * p), 2523.0f / 32.0f);
    return float4(pq, 1.0f);
}
