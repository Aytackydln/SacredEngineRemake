cbuffer transfer_constants : register(b0)
{
    float transfer_exponent;
    float reference_white_scrgb;
};
Texture2D<float4> scene_snapshot : register(t0);

float4 vs_main(uint vertex : SV_VertexID) : SV_Position
{
    return float4(vertex == 2 ? 3.0f : -1.0f, vertex == 1 ? 3.0f : -1.0f, 0.0f, 1.0f);
}

float4 ps_main(float4 position : SV_Position) : SV_Target
{
    float4 scene = scene_snapshot.Load(int3(position.xy, 0));
    float3 rgb = transfer_exponent < 1.0f
        ? LinearToHdrArt(scene.rgb / reference_white_scrgb)
        : HdrArtToLinear(scene.rgb) * reference_white_scrgb;
    return float4(rgb, scene.a);
}
