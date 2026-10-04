// Persistent native particle state. Birth randomization stays on CPU.
struct ParticleState
{
    float3 position; float3 velocity;
    float gravity; float size; float fade; float rotation; float angular_velocity;
    int atlas_cell; int draw_order;
};
struct ParticleBirth { uint slot; ParticleState state; };
RWStructuredBuffer<ParticleState> particle_states : register(u0);
RWStructuredBuffer<ParticleDraw> particle_output : register(u1);
StructuredBuffer<ParticleBirth> particle_births : register(t5);
StructuredBuffer<uint> particle_colors : register(t6);
StructuredBuffer<ParticleDraw> cpu_particles : register(t7);
cbuffer ParticleCompute : register(b1)
{
    uint capacity; uint output_offset; uint birth_count; uint sort_size;
    float step_time; float fade_rate; float size_rate; float gravity_rate;
    float3 gravity_direction; float inward_acceleration;
    float angular_rate; float ground_height; uint collision; uint sort_j;
    uint sort_k; uint sequence_base; uint atlas_side; uint texture_slot;
    float4 projection;
    float2 emitter_origin; float2 depth_anchor;
    float emitter_height; uint blend_flags; uint use_colors; uint random_atlas;
    float4 screen_transform;
    uint texture_encoding; uint cpu_count; uint unused0; uint unused1;
};
[numthreads(64,1,1)]
void clear_states(uint i : SV_DispatchThreadID)
{
    if (i < capacity) particle_states[i] = (ParticleState)0;
}
[numthreads(64,1,1)]
void spawn_particles(uint i : SV_DispatchThreadID)
{
    if (i < birth_count) particle_states[particle_births[i].slot] = particle_births[i].state;
}
[numthreads(64,1,1)]
void advance_particles(uint i : SV_DispatchThreadID)
{
    if (i >= capacity) return;
    precise ParticleState p = particle_states[i];
    if (p.fade <= 0 || p.size <= 0) return;
    p.position += p.velocity * step_time;
    float acceleration = p.gravity * step_time;
    p.velocity.z -= acceleration;
    p.velocity += gravity_direction * acceleration;
    if (collision == 0 && inward_acceleration != 0)
    {
        float distance = length(p.position);
        if (distance >= 0.0000001f)
            p.velocity -= p.position * (1 / distance) * (inward_acceleration * step_time);
    }
    p.rotation += p.angular_velocity * step_time;
    if (collision != 0 && p.position.z <= ground_height)
    {
        if (collision == 2) p.fade = 0;
        else p.velocity.z = -p.velocity.z;
    }
    p.fade += fade_rate * step_time;
    p.size += size_rate * step_time;
    p.gravity += gravity_rate * step_time;
    p.rotation += angular_rate * step_time;
    particle_states[i] = p;
}
[numthreads(64,1,1)]
void clear_draws(uint i : SV_DispatchThreadID)
{
    if (i < sort_size) particle_output[i] = (ParticleDraw)0;
}
[numthreads(64,1,1)]
void copy_cpu_draws(uint i : SV_DispatchThreadID)
{
    if (i < cpu_count) particle_output[i] = cpu_particles[i];
}
[numthreads(64,1,1)]
void project_particles(uint i : SV_DispatchThreadID)
{
    if (i >= capacity) return;
    ParticleState p = particle_states[i];
    precise ParticleDraw draw = (ParticleDraw)0;
    if (p.fade <= 0 || p.size <= 0 || texture_slot == 0xffffffff) { particle_output[output_offset+i] = draw; return; }
    precise float2 iso = float2(p.position.x * projection.x, (p.position.y * projection.z) * projection.y);
    precise float2 ground = float2((iso.y / 24 + iso.x / 48) * .5f, (iso.y / 24 - iso.x / 48) * .5f);
    precise float2 world = emitter_origin + ground;
    precise float height = (emitter_height + p.position.z) * projection.w * projection.y;
    precise float2 anchor = float2((world.x-world.y)*48+48, (world.x+world.y)*24);
    precise float width = 2 * p.size * projection.x;
    precise float tall = 2 * p.size * projection.y;
    draw.sort_depth = (depth_anchor.x+ground.x)+(depth_anchor.y+ground.y)+height/96;
    draw.tile_x = (int)floor(world.x); draw.tile_y = (int)floor(world.y);
    draw.draw_order = p.draw_order; draw.sequence = sequence_base + i;
    SpriteInstance s = (SpriteInstance)0;
    s.rect = float4(screen_transform.xy+(anchor-float2(width*.5f,height+tall*.5f))*screen_transform.z,
        float2(width,tall)*screen_transform.z);
    s.depth = clamp(.5f-(draw.sort_depth-screen_transform.w)/4096, .2f, .72f);
    s.texture_index = texture_slot; s.frame_count = atlas_side*atlas_side;
    uint fade = (uint)clamp((int)p.fade, 0, 255);
    uint color = use_colors != 0 ? particle_colors[fade] : (particle_colors[0]&0xffffff)|(fade<<24);
    s.corner_alpha = float4((color>>16)&255, (color>>8)&255, color&255, color>>24)/255;
    s.texture_variant = random_atlas != 0 ? p.atlas_cell : clamp((int)((255-p.fade)*s.frame_count/256),0,(int)s.frame_count-1);
    s.particle_sprite = 1 | blend_flags; s.particle_rotation = p.rotation;
    s.atlas_columns = s.atlas_rows = atlas_side;
    draw.sprite = s; draw.encoding = texture_encoding; draw.valid = 1;
    particle_output[output_offset+i] = draw;
}
bool particle_less(ParticleDraw a, ParticleDraw b)
{
    if (a.valid != b.valid) return a.valid > b.valid;
    if (a.sort_depth != b.sort_depth) return a.sort_depth < b.sort_depth;
    if (a.tile_y != b.tile_y) return a.tile_y < b.tile_y;
    if (a.tile_x != b.tile_x) return a.tile_x < b.tile_x;
    if (a.draw_order != b.draw_order) return a.draw_order < b.draw_order;
    return a.sequence < b.sequence;
}
[numthreads(64,1,1)]
void sort_particles(uint i : SV_DispatchThreadID)
{
    uint other = i ^ sort_j;
    if (i >= sort_size || other <= i || other >= sort_size) return;
    ParticleDraw a = particle_output[i], b = particle_output[other];
    bool ascending = (i & sort_k) == 0;
    if (ascending ? particle_less(b,a) : particle_less(a,b))
    { particle_output[i] = b; particle_output[other] = a; }
}
[earlydepthstencil]
pixel_output ps_gpu_sdr(vertex_output input) { return render_unlit_sdr(input,1); }
[earlydepthstencil]
particle_pixel_output ps_gpu_hdr(vertex_output input)
{
    if (input.texture_encoding == 0) return render_unlit_hdr_alpha_mask(input,1);
    if (input.texture_encoding == 1) return render_unlit_hdr_argb(input,1);
    return render_unlit_hdr_rgb(input,1);
}
