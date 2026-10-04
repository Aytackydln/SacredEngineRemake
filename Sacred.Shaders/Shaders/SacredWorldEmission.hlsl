// GPU-owned world emitter. Source and all counts are validated before publication.
ByteAddressBuffer src : register(t0);
RWByteAddressBuffer state : register(u0);
cbuffer Control : register(b0) {
    float dt; float ground; uint capacity; uint sets;
    uint set_stride; uint state_bytes; uint initial_offset; uint unused;
};
uint set_start(uint s) { return 256+s*set_stride; }
uint particle_start(uint s,uint slot) { return set_start(s)+16+8*capacity+52*slot; }
uint param_start(uint s) { return 32+144*s; }
float sf(uint p) { return asfloat(src.Load(p)); }
float vf(uint p) { return asfloat(state.Load(p)); }
void putf(uint p,float v) { state.Store(p,asuint(v)); }
float rounded(double v) {
    double magnitude=abs(v);float f=float(magnitude);if(!isfinite(f))return float(v);
    uint bits=asuint(f);float lo=bits==0?0:asfloat(bits-1),hi=asfloat(bits+1);
    double lower=(double(lo)+double(f))*.5,upper=(double(hi)+double(f))*.5;bool odd=(bits&1)!=0;
    if(magnitude<lower || (odd && magnitude==lower))f=lo;
    else if(magnitude>upper || (odd && magnitude==upper))f=hi;
    return v<0?-f:f;
}
double random_sample() {
    uint a=state.Load(224)+1, b=state.Load(228)+1;
    if(a>=56)a=1; if(b>=56)b=1;
    int value=int(state.Load(a*4))-int(state.Load(b*4));
    if(value==2147483647)value--; if(value<0)value+=2147483647;
    state.Store(a*4,uint(value)); state.Store(224,a); state.Store(228,b);
    return double(value)*(1.0/2147483647.0);
}
float random_scalar(float center,float width) { return center+(rounded(random_sample())*2-1)*width; }
float3 random_vector(uint center,uint width) {
    // Explicit statements preserve RNG order; function argument evaluation order is unspecified.
    float x=random_scalar(sf(center),sf(width));
    float y=random_scalar(sf(center+4),sf(width+4));
    float z=random_scalar(sf(center+8),sf(width+8)); return float3(x,y,z);
}
struct Particle { float3 position,velocity; float gravity,size,fade,rotation,angular; int atlas,order; };
Particle read_particle(uint p) {
    Particle v; v.position=asfloat(state.Load3(p));v.velocity=asfloat(state.Load3(p+12));
    v.gravity=vf(p+24);v.size=vf(p+28);v.fade=vf(p+32);v.rotation=vf(p+36);v.angular=vf(p+40);
    v.atlas=int(state.Load(p+44));v.order=int(state.Load(p+48));return v;
}
void write_particle(uint p,Particle v) {
    state.Store3(p,asuint(v.position));state.Store3(p+12,asuint(v.velocity));
    putf(p+24,v.gravity);putf(p+28,v.size);putf(p+32,v.fade);putf(p+36,v.rotation);putf(p+40,v.angular);
    state.Store(p+44,uint(v.atlas));state.Store(p+48,uint(v.order));
}
float3 direction(uint s) {
    uint p=param_start(s);
    return src.Load(20)!=0 && int(src.Load(p+132))<2 ? float3(sf(24),0,0) : asfloat(src.Load3(p+116));
}
Particle advance(Particle v,uint s,float time,bool normal) {
    uint p=param_start(s);v.position+=v.velocity*time;
    float acc=v.gravity*time;v.velocity.z-=acc;v.velocity+=direction(s)*acc;
    uint collision=normal?src.Load(16):0;
    if(normal && collision==0 && sf(p+128)!=0) {
        float len=length(v.position);
        if(len>=.0000001f)v.velocity-=(v.position*(1/len))*(sf(p+128)*time);
    }
    v.rotation+=v.angular*time;
    if(collision!=0 && v.position.z<=ground) {if(collision==2)v.fade=0;else v.velocity.z=-v.velocity.z;}
    v.fade+=sf(p+104)*time;v.size+=sf(p+108)*time;v.gravity+=sf(p+100)*time;v.rotation+=sf(p+112)*time;
    return v;
}
uint select_set() {
    uint mode=src.Load(8);
    if(mode==2)return random_sample()<.8?0:1;
    if(mode!=3)return 0;
    double selection=random_sample()*double(sf(28));
    for(uint s=0;s<sets;s++) {selection-=double(rounded(1.0/double(sf(param_start(s)+88))));if(selection<0)return s;}
    return sets-1;
}
void emit() {
    float interval=sf(param_start(0)+88);if(interval<=0)return;
    float elapsed=vf(232)+dt;uint total=state.Load(240);
    // No random attempts beyond shared remaining capacity, even for a tiny interval.
    uint available=capacity-min(total,capacity);
    for(uint n=0;n<available && elapsed>=interval;n++) {
        elapsed-=interval;uint s=select_set();s=min(s,sets-1);uint base=set_start(s),p=param_start(s);
        uint free_count=state.Load(base+4),live_count=state.Load(base);
        if(free_count==0 || live_count>=capacity)break;
        uint slot=state.Load(base+16+4*capacity+4*(free_count-1));slot=min(slot,capacity-1);
        Particle v;v.position=random_vector(p+56,p+68);v.velocity=random_vector(p+32,p+44);
        v.gravity=random_scalar(sf(p),sf(p+4));v.size=random_scalar(sf(p+8),sf(p+12));
        v.rotation=random_scalar(sf(p+16),sf(p+20));v.angular=random_scalar(sf(p+24),sf(p+28));
        uint variants=src.Load(p+96)&255;
        if(variants==0)v.atlas=0;
        else if(variants==255)v.atlas=int(src.Load(p+132));
        else v.atlas=int(random_sample()*double(variants));
        v.fade=255;v.order=int(state.Load(236));state.Store(236,uint(v.order)+1);
        v=advance(v,s,elapsed,false);write_particle(particle_start(s,slot),v);
        state.Store(base+16+4*live_count,slot);state.Store(base,live_count+1);state.Store(base+4,free_count-1);total++;
    }
    if(total==capacity)elapsed=min(elapsed,interval);
    putf(232,elapsed);state.Store(240,total);
}
[numthreads(64,1,1)]
void initialize(uint i:SV_DispatchThreadID) {
    if(i>=state_bytes/4)return;
    uint safe_i=min(i,state_bytes/4-1);state.Store(safe_i*4,src.Load(initial_offset+safe_i*4));
}
[numthreads(1,1,1)]
void emit_before(uint i:SV_DispatchThreadID) {if(src.Load(12)!=0)emit();}
[numthreads(64,1,1)]
void movement(uint i:SV_DispatchThreadID) {
    if(i>=sets*capacity)return;i=min(i,sets*capacity-1);uint s=i/capacity,slot=i%capacity,p=particle_start(s,slot);
    Particle v=read_particle(p);if(v.fade<=0 || v.size<=0)return;
    v=advance(v,s,dt,true);write_particle(p,v);
}
[numthreads(1,1,1)]
void release_and_emit(uint i:SV_DispatchThreadID) {
    uint total=0;
    for(uint s=0;s<sets;s++) {
        uint base=set_start(s),count=min(state.Load(base),capacity),free_count=min(state.Load(base+4),capacity),write=0;
        for(uint j=0;j<count;j++) {
            uint slot=min(state.Load(base+16+4*j),capacity-1);Particle v=read_particle(particle_start(s,slot));
            if(v.fade<=0 || v.size<=0) {
                if(free_count<capacity)state.Store(base+16+4*capacity+4*free_count++,slot);
            } else state.Store(base+16+4*write++,slot);
        }
        state.Store(base,write);state.Store(base+4,free_count);total+=write;
    }
    state.Store(240,total);if(src.Load(12)==0)emit();
}
