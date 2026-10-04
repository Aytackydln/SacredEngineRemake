// Row-vector matrices and the managed reference's independent track ranges.
// Reference algorithms: .NET runtime (MIT), see docs/phase-8-validation.md.
cbuffer Controls : register(b0) {
    float requested_time; float duration; uint time_mode; uint animation_count;
    uint bone_count; uint hierarchy_level; uint skin_offset; uint reference_fma;
};
ByteAddressBuffer source : register(t0);
RWByteAddressBuffer output_pose : register(u0);
float4x4 identity_matrix() { return float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1); }
float dot4_reference(float4 a,float4 b) {float4 p=a*b;return (p.x+p.y)+(p.z+p.w);}
float dot3_reference(float3 a,float3 b) {float3 p=a*b;return (p.x+p.y)+p.z;}
float round_reference(double value) {
    double magnitude=abs(value);float s=float(magnitude);
    if(!isfinite(s)) return float(value);
    uint bits=asuint(s);float lower=bits==0?0:asfloat(bits-1),upper=asfloat(bits+1);
    double lo=(double(lower)+double(s))*0.5,hi=(double(upper)+double(s))*0.5;
    bool odd=(bits&1)!=0;
    if(magnitude<lo || (odd && magnitude==lo)) s=lower;
    else if(magnitude>hi || (odd && magnitude==hi)) s=upper;
    return value<0?-s:s;
}
float divide_reference(float a,float b) { return round_reference(double(a)/double(b)); }
float sqrt_reference(float v) {
    float s=sqrt(v);
    if(v<=0 || !isfinite(v)) return s;
    uint bits=asuint(s);float lower=asfloat(bits-1),upper=asfloat(bits+1);
    double low_mid=(double(s)+double(lower))*0.5;
    double high_mid=(double(s)+double(upper))*0.5;
    double value=double(v);bool odd=(bits&1)!=0;
    if(value<low_mid*low_mid || (odd && value==low_mid*low_mid)) return lower;
    if(value>high_mid*high_mid || (odd && value==high_mid*high_mid)) return upper;
    return s;
}
float4 normalize_reference(float4 q) {
    float length_value=sqrt_reference(dot4_reference(q,q));
    return float4(divide_reference(q.x,length_value),divide_reference(q.y,length_value),
        divide_reference(q.z,length_value),divide_reference(q.w,length_value));
}
float3 normalize_basis(float3 v) {
    float len=sqrt_reference(dot3_reference(v,v));
    return float3(divide_reference(v.x,len),divide_reference(v.y,len),divide_reference(v.z,len));
}
float4 multiply_add_reference(float4 a,float b,float4 c) {
    if(reference_fma==0) return a*b+c;
    return float4(round_reference(double(a.x)*double(b)+double(c.x)),round_reference(double(a.y)*double(b)+double(c.y)),
        round_reference(double(a.z)*double(b)+double(c.z)),round_reference(double(a.w)*double(b)+double(c.w)));
}
float4 transform_reference(float4 v,float4x4 m) {
    float4 result=m[0]*v.x;
    result=multiply_add_reference(m[1],v.y,result);result=multiply_add_reference(m[2],v.z,result);
    return multiply_add_reference(m[3],v.w,result);
}
float4x4 multiply_reference(float4x4 a,float4x4 b) {
    return float4x4(transform_reference(a[0],b),transform_reference(a[1],b),
        transform_reference(a[2],b),transform_reference(a[3],b));
}
float4x4 source_matrix(uint p) {
    return float4x4(asfloat(source.Load4(p)),asfloat(source.Load4(p+16)),
        asfloat(source.Load4(p+32)),asfloat(source.Load4(p+48)));
}
float4x4 output_matrix(uint p) {
    return float4x4(asfloat(output_pose.Load4(p)),asfloat(output_pose.Load4(p+16)),
        asfloat(output_pose.Load4(p+32)),asfloat(output_pose.Load4(p+48)));
}
void store_matrix(uint p, float4x4 m) {
    output_pose.Store4(p,asuint(m[0])); output_pose.Store4(p+16,asuint(m[1]));
    output_pose.Store4(p+32,asuint(m[2])); output_pose.Store4(p+48,asuint(m[3]));
}
float4 normalized_rotation(float4 q) {
    float squared=dot4_reference(q,q);
    return squared>0.000001 && all(isfinite(q)) ? normalize_reference(q) : float4(0,0,0,1);
}
float4x4 rotation_matrix(float4 q) {
    float x=q.x,y=q.y,z=q.z,w=q.w;
    return float4x4(1-2*(y*y+z*z),2*(x*y+z*w),2*(x*z-y*w),0,
        2*(x*y-z*w),1-2*(z*z+x*x),2*(y*z+x*w),0,
        2*(x*z+y*w),2*(y*z-x*w),1-2*(y*y+x*x),0, 0,0,0,1);
}
float sample_time() {
    if(time_mode==1) return clamp(requested_time,0,max(0,duration));
    if(!isfinite(requested_time)||!isfinite(duration)||duration<=0.000001) return 0;
    // HLSL fmod can change an already-in-range sample through quotient rounding.
    // Binary subtraction preserves the reference remainder for finite float inputs.
    float magnitude=abs(requested_time);
    if(magnitude<duration) return requested_time<0?requested_time+duration:requested_time;
    int shift=int((asuint(magnitude)>>23)&255)-int((asuint(duration)>>23)&255);
    if(int((asuint(duration)>>23)&255)+shift>=255) shift--;
    float divisor=asfloat(asuint(duration)+(uint(shift)<<23));
    for(int bit=0;bit<256 && bit<=shift;bit++) {
        if(magnitude>=divisor) magnitude-=divisor;
        divisor*=0.5;
    }
    return requested_time<0 && magnitude>0?duration-magnitude:magnitude;
}
uint2 keys(uint4 range, float t, out float amount) {
    uint count=min(range.z,range.w); amount=0;
    if(count<=1) return uint2(0,0);
    if(t<=asfloat(source.Load(range.x))) return uint2(0,0);
    if(t>=asfloat(source.Load(range.x+(count-1)*4))) return uint2(count-1,count-1);
    uint lo=0,hi=count-1;
    // At most 32 bisections for a uint range, including divergent lanes.
    for(uint iteration=0;iteration<32 && hi-lo>1;iteration++) { uint mid=lo+(hi-lo)/2;
        if(asfloat(source.Load(range.x+mid*4))<=t) lo=mid; else hi=mid;
    }
    float left=asfloat(source.Load(range.x+lo*4));
    float span=asfloat(source.Load(range.x+hi*4))-left;
    amount=isfinite(span)&&span>0.000001?clamp(divide_reference(t-left,span),0,1):0;
    return uint2(lo,hi);
}
float3 sample_vector(uint4 r,float t,float3 fallback) {
    if(r.w==0) return fallback;
    float a; uint2 k=keys(r,t,a); float3 l=asfloat(source.Load3(r.y+k.x*12));
    if(k.x==k.y) return l;
    return multiply_add_reference(float4(l,0),1-a,float4(asfloat(source.Load3(r.y+k.y*12))*a,0)).xyz;
}
float sin_reference(float v) {
    // Slerp's shortest-path angles are in [0, pi/2]. Evaluate in double, then round once.
    double x=double(v),z=x*x,term=x,sum=x;
    [unroll] for(uint n=1;n<=12;n++) { term*=(-z)/double((2*n)*(2*n+1));sum+=term; }
    return round_reference(sum);
}
float4 sample_rotation(uint4 r,float t,float4 fallback) {
    if(r.w==0) return fallback;
    float a; uint2 k=keys(r,t,a); float4 l=asfloat(source.Load4(r.y+k.x*32));
    if(k.x==k.y) return l;
    float4 right=asfloat(source.Load4(r.y+k.y*32));
    float4 parameters=asfloat(source.Load4(r.y+k.x*32+16));
    float s1,s2;
    if(parameters.w!=0) { s1=1-a; s2=a*parameters.z; }
    else {
        s1=sin_reference((1-a)*parameters.x)*parameters.y;
        s2=sin_reference(a*parameters.x)*parameters.y*parameters.z;
    }
    float4 result=s1*l+s2*right; return normalize_reference(result);
}
float4x4 sample_scale(uint4 r,float t,float4x4 fallback) {
    if(r.w==0) return fallback;
    float a; uint2 k=keys(r,t,a); float4x4 l=source_matrix(r.y+k.x*64);
    if(k.x==k.y) return l;
    float4x4 right=source_matrix(r.y+k.y*64);
    float4x4 result=float4x4(multiply_add_reference(l[0],1-a,right[0]*a),multiply_add_reference(l[1],1-a,right[1]*a),
        multiply_add_reference(l[2],1-a,right[2]*a),multiply_add_reference(l[3],1-a,right[3]*a));
    result[0].w=0;result[1].w=0;result[2].w=0;result[3]=float4(0,0,0,1);return result;
}
// System.Numerics decomposition ranking, including ties and degenerate basis repair.
uint3 ranked(float3 s) {
    if(s.x<s.y) { if(s.y<s.z) return uint3(2,1,0);
        return s.x<s.z?uint3(1,2,0):uint3(1,0,2); }
    if(s.x<s.z) return uint3(2,0,1);
    return s.y<s.z?uint3(0,2,1):uint3(0,1,2);
}
float4 matrix_rotation(float4x4 m) {
    float trace=m[0].x+m[1].y+m[2].z; float s,inv;
    if(trace>0) { s=sqrt(trace+1); inv=0.5/s;
        return float4((m[1].z-m[2].y)*inv,(m[2].x-m[0].z)*inv,(m[0].y-m[1].x)*inv,s*0.5); }
    if(m[0].x>=m[1].y && m[0].x>=m[2].z) { s=sqrt(1+m[0].x-m[1].y-m[2].z); inv=0.5/s;
        return float4(0.5*s,(m[0].y+m[1].x)*inv,(m[0].z+m[2].x)*inv,(m[1].z-m[2].y)*inv); }
    if(m[1].y>m[2].z) { s=sqrt(1+m[1].y-m[0].x-m[2].z); inv=0.5/s;
        return float4((m[1].x+m[0].y)*inv,0.5*s,(m[2].y+m[1].z)*inv,(m[2].x-m[0].z)*inv); }
    s=sqrt(1+m[2].z-m[0].x-m[1].y); inv=0.5/s;
    return float4((m[2].x+m[0].z)*inv,(m[2].y+m[1].z)*inv,0.5*s,(m[0].y-m[1].x)*inv);
}
float4x4 rigid_or_original(float4x4 m) {
    float3 basis[3]={m[0].xyz,m[1].xyz,m[2].xyz};
    float3 canonical[3]={float3(1,0,0),float3(0,1,0),float3(0,0,1)};
    float3 scales=float3(sqrt_reference(dot3_reference(basis[0],basis[0])),sqrt_reference(dot3_reference(basis[1],basis[1])),sqrt_reference(dot3_reference(basis[2],basis[2])));
    uint3 order=ranked(scales);uint a=order.x,b=order.y,c=order.z;
    if(scales[a]<0.0001) basis[a]=canonical[a];
    basis[a]=normalize_basis(basis[a]);
    if(scales[b]<0.0001) { float3 f=abs(basis[a]);
        uint cc=f.x<f.y?(f.y<f.z?0:(f.x<f.z?0:2)):(f.x<f.z?1:(f.y<f.z?1:2));
        basis[b]=cross(basis[a],canonical[cc]); }
    basis[b]=normalize_basis(basis[b]);
    if(scales[c]<0.0001) basis[c]=cross(basis[a],basis[b]);
    basis[c]=normalize_basis(basis[c]);
    float det=dot(basis[0],cross(basis[1],basis[2]));
    if(det<0) { basis[a]=-basis[a]; det=-det; }
    float error=det-1;
    if(error*error>0.0001) return m;
    float4x4 normalized=float4x4(float4(basis[0],0),float4(basis[1],0),float4(basis[2],0),float4(0,0,0,1));
    float4 q=matrix_rotation(normalized);float squared=dot4_reference(q,q);
    if(!all(isfinite(q))||!all(isfinite(m[3].xyz))||squared<=0.000001) return m;
    float4x4 result=rotation_matrix(normalize_reference(q));result[3].xyz=m[3].xyz;return result;
}
[numthreads(64,1,1)] void sample_locals(uint3 id:SV_DispatchThreadID) {
    uint i=id.x;if(i>=animation_count)return;uint p=i*224;
    if(source.Load(p+208)==0) {store_matrix(i*64,source_matrix(p+144));return;}
    float t=sample_time();
    float3 translation=sample_vector(source.Load4(p),t,asfloat(source.Load3(p+48)));
    float4 rotation=sample_rotation(source.Load4(p+16),t,asfloat(source.Load4(p+64)));
    float4x4 scale=sample_scale(source.Load4(p+32),t,source_matrix(p+80));
    float4x4 move=identity_matrix();move[3].xyz=translation;
    store_matrix(i*64,multiply_reference(multiply_reference(scale,rotation_matrix(normalized_rotation(rotation))),move));
}
[numthreads(64,1,1)] void evaluate_world(uint3 id:SV_DispatchThreadID) {
    uint i=id.x;if(i>=bone_count)return;uint p=skin_offset+i*272;uint4 info=source.Load4(p);
    if(info.w!=hierarchy_level)return;
    float4x4 world;
    if(info.z==0) world=source_matrix(p+80);
    else {
        // HLSL selections can evaluate both operands. Every address must remain valid
        // even for absent mappings, self roots and invalid authored parent indices.
        uint mapped=min(info.x,max(1,animation_count)-1);
        uint parent=min(info.y,max(1,bone_count)-1);
        float4x4 local=asint(info.x)>=0?output_matrix(mapped*64):source_matrix(p+16);
        world=info.z==1?local:multiply_reference(local,output_matrix((animation_count+parent)*64));}
    store_matrix((animation_count+i)*64,world);
}
[numthreads(64,1,1)] void create_palettes(uint3 id:SV_DispatchThreadID) {
    uint i=id.x;uint count=max(1,bone_count);if(i>=count)return;
    uint regular=(animation_count+count+i)*64;uint rigid=regular+count*64;
    if(bone_count==0) {store_matrix(regular,identity_matrix());store_matrix(rigid,identity_matrix());return;}
    uint p=skin_offset+i*272;float4x4 world=output_matrix((animation_count+i)*64);
    store_matrix(regular,multiply_reference(source_matrix(p+144),world));
    store_matrix(rigid,multiply_reference(source_matrix(p+208),rigid_or_original(world)));
}
