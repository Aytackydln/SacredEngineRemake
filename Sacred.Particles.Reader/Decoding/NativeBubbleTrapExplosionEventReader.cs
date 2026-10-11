using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeBubbleTrapExplosionEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x798460) yield break;
        var size=(int)code.At(entry.FactoryAddress).GetImmediate(0);
        var family=new NativeParticleFamily("Bubble Trap Explosion",entry.FactoryAddress,0x798460,0x7984D3,0x7985D0,size,0,0,0,0);
        var bindings=NativeTextureReader.ReadBindings(image,code,family);
        var memory=NativePresetReader.Evaluate(image,code,family,0,quality,token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        uint Immediate(uint address,int operand=0) => (uint)code.At(address).GetImmediate(operand);
        var d=new SacredBubbleTrapExplosionFxDefinition(bindings.Single().TextureName,NativeFxRingReader.Read(image,code,quality,token,false))
        {
            InitialState=MemoryMarshal.Read<SacredBubbleTrapExplosionFxStateLayout>(memory.ObjectBytes(0x20A0,160)),
            Lifetime=Operand(0x798516), RadiusRate=Operand(0x798504), RingWidth=Operand(0x79858E),
            WeightRate=2, // Native fadd ST0,ST0 at 798541.
            RedBlueScale=Operand(0x798545), GreenScale=Operand(0x798574), SegmentCount=(int)Immediate(0x798572),
            SourceColorOnly=Immediate(0x798553)!=0, Additive=Immediate(0x798553)!=0,
            InitialNotification=Immediate(0x798630,1), InitialLightStrength=Immediate(0x79863A,1)
        };
        yield return entry with { NativeClass=family.Name,Preset=0,IsEventPreset=true,OneTime=true,
            Status=SacredParticleDefinitionStatus.Decoded,Diagnostic=null,BubbleTrapExplosion=d,TextureBindings=bindings,
            Capacity=NativeParticleCapacityReader.Read(code,entry.FactoryAddress),DisplayName="Native expanding 50-segment ring and strict half-second cutoff" };
    }
}
