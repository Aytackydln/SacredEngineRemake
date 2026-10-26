// Buffer adapter only. skin_vertex and its weighting/projection rules come from
// the desktop's SacredSkinning.hlsl. Byte loads preserve its packed 48-byte ABI
// when translating to WGSL, whose vec3 storage alignment is different.
SkinVertex browser_skin_vertex(uint index)
{
    uint address = index * 48;
    SkinVertex vertex;
    vertex.position = asfloat(skin_vertices.Load3(address));
    vertex.normal = asfloat(skin_vertices.Load3(address + 12));
    vertex.uv = asfloat(skin_vertices.Load2(address + 24));
    vertex.influence_offset = skin_vertices.Load(address + 32);
    vertex.influence_count = skin_vertices.Load(address + 36);
    vertex.rigid = skin_vertices.Load(address + 40);
    vertex.padding = 0;
    return vertex;
}

RWByteAddressBuffer browser_skinned : register(u0);
[numthreads(64, 1, 1)]
void skin_main(uint3 thread : SV_DispatchThreadID)
{
    if (thread.x >= skin_projection[0].vertices) return;
    float3 position, normal; float2 uv;
    skin_vertex(thread.x, position, normal, uv);
    uint address = thread.x * 32;
    browser_skinned.Store3(address, asuint(position));
    browser_skinned.Store3(address + 12, asuint(normal));
    browser_skinned.Store2(address + 24, asuint(uv));
}
