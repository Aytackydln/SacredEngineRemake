struct SkinVertex
{
    float3 position; float3 normal; float2 uv;
    uint influence_offset; uint influence_count; uint rigid; uint padding;
};
struct SkinInfluence { int bone; float weight; };
struct SkinProjection { float3 offset; uint horizontal0; uint horizontal1; uint vertical; uint bones; uint vertices; };
struct SkinMatrix { row_major float4x4 value; };
StructuredBuffer<SkinVertex> skin_vertices : register(t3);
StructuredBuffer<SkinInfluence> skin_influences : register(t4);
StructuredBuffer<SkinProjection> skin_projection : register(t5);
StructuredBuffer<SkinMatrix> skin_regular : register(t6);
StructuredBuffer<SkinMatrix> skin_rigid : register(t7);

void skin_vertex(uint index, out float3 position, out float3 normal, out float2 uv)
{
    SkinVertex vertex = skin_vertices[index];
    SkinProjection projection = skin_projection[0];
    precise float3 weighted_position = 0;
    precise float3 weighted_normal = 0;
    precise float total = 0;
    [loop] for (uint i = 0; i < vertex.influence_count; i++)
    {
        SkinInfluence influence = skin_influences[vertex.influence_offset + i];
        if ((uint)influence.bone >= projection.bones || !isfinite(influence.weight) || influence.weight <= 0) continue;
        row_major float4x4 transform = vertex.rigid != 0 ? skin_rigid[influence.bone].value : skin_regular[influence.bone].value;
        weighted_position += mul(float4(vertex.position, 1), transform).xyz * influence.weight;
        weighted_normal += mul(float4(vertex.normal, 0), transform).xyz * influence.weight;
        total += influence.weight;
    }
    float3 raw_position = total > 0.000001f ? weighted_position / total : vertex.position;
    float3 raw_normal = total > 0.000001f ? weighted_normal / total : vertex.normal;
    position = float3(raw_position[projection.horizontal0], raw_position[projection.horizontal1], raw_position[projection.vertical]) + projection.offset;
    normal = float3(raw_normal[projection.horizontal0], raw_normal[projection.horizontal1], raw_normal[projection.vertical]);
    normal = dot(normal, normal) > 0.000001f ? normalize(normal) : float3(0, 0, 1);
    uv = vertex.uv;
}
