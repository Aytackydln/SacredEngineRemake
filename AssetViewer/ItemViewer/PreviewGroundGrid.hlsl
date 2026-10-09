cbuffer GridConstants : register(b0)
{
    row_major float4x4 transform;
    float4 geometry; // extent, cell size, fade start, fade end
    float4 style; // opacity, world line width
}

struct VertexOutput
{
    float4 position : SV_Position;
    float2 plane : TEXCOORD;
};

VertexOutput vs_main(uint id : SV_VertexID)
{
    static const float2 corners[6] = {
        float2(-1, -1), float2(1, -1), float2(1, 1),
        float2(-1, -1), float2(1, 1), float2(-1, 1)
    };
    VertexOutput output;
    output.plane = corners[id] * geometry.x;
    output.position = mul(float4(output.plane, 0, 1), transform);
    return output;
}

float4 ps_main(VertexOutput input) : SV_Target
{
    float2 cells = input.plane / geometry.y;
    float2 footprint = max(fwidth(cells), 0.00001);
    float2 distanceToLine = abs(frac(cells + 0.5) - 0.5);
    float lineWidth = style.y / geometry.y;
    float2 coverage = saturate((lineWidth * 0.5 - distanceToLine) / footprint + 0.5);
    // Derivatives provide antialiasing only; they never resize the lattice or lines.
    coverage *= saturate(lineWidth / footprint);
    float fade = 1 - smoothstep(geometry.z, geometry.w, length(input.plane));
    float alpha = max(coverage.x, coverage.y) * fade * style.x;
    return float4(1, 1, 1, alpha);
}
