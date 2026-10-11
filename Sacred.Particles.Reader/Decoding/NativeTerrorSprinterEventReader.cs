using System.Globalization;
using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeTerrorSprinterEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7C1050) yield break;
        var size=(int)code.At(entry.FactoryAddress).GetImmediate(0);
        uint Immediate(uint address,int operand=0) => (uint)code.At(address).GetImmediate(operand);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float ImmediateFloat(uint address,int operand=0) => BitConverter.UInt32BitsToSingle(Immediate(address,operand));
        var format=image.String(Immediate(0x7C10D1),128);
        var variants=Enumerable.Range(1,(int)Immediate(0x7C10C4,1)).Select(i =>
            format.Replace("%d",i.ToString(CultureInfo.InvariantCulture),StringComparison.Ordinal)).ToArray();
        var ring=NativeFxRingReader.Read(image,code,quality,token,true);
        for (var mode=0;mode<2;mode++)
        {
            var family=new NativeParticleFamily("Terror Sprinter",entry.FactoryAddress,0x7C1050,0x7C110E,0x7C1390,size,0,0,0,0)
            {
                ConstructorRegister=Register.EDI,InitializerEnd=mode==0 ? 0x7C13CEu : 0x7C144Du,
                ActorLookup=mode==0 ? null : (0x7C13CEu,0x7C13FEu),
                EventArguments=new Dictionary<int,uint> { [0x70]=BitConverter.SingleToUInt32Bits(1) },
                SampleCombatArtDurationBase=10
            };
            var bindings=NativeTextureReader.ReadBindings(image,code,family).ToList();
            var memory=NativePresetReader.Evaluate(image,code,family,mode,quality,token);
            var state=MemoryMarshal.Read<SacredTerrorSprinterFxStateLayout>(memory.ObjectBytes(0x20A0,16));
            if (mode==0) state.Lifetime=ImmediateFloat(0x7C1474,1);
            var d=new SacredTerrorSprinterFxDefinition(new(bindings.Single().TextureName,
                ImmediateFloat(mode==0 ? 0x7C131Fu : 0x7C1242u),0,default,Operand(0x7C1200),true,true),
                Array.AsReadOnly(variants),ring)
            {
                InitialState=state,WeightRate=2, // Native fadd ST0,ST0 at 7C1191 and 7C114A.
                RedBlueScale=Operand(mode==0 ? 0x7C12CCu : 0x7C11EFu),GreenScale=Operand(mode==0 ? 0x7C12FBu : 0x7C121Eu),
                RingRadius=Operand(mode==0 ? 0x7C1351u : 0x7C1274u), RingHalfWidth=Operand(mode==0 ? 0x7C1331u : 0x7C1254u),
                SegmentCount=(int)Immediate(0x7C126A),RingColor=Immediate(0x7C127A),RingSourceColorOnly=false,RingAdditive=Immediate(0x7C125D)!=0,
                InitialNotification=Immediate(mode==0 ? 0x7C14E6u : 0x7C1428u,1),
                PendingCode=mode==0 ? 0 : Immediate(0x7C1435,1),TextureCommand=0 // EBX is explicitly cleared at 7C139D.
            };
            // Resource slots are randomized in the native constructor, not in initialize or update.
            var sample=new SacredTerrorSprinterFxPlayback(d);
            for (var i=0;i<d.SegmentCount;i++) bindings.Add(new(0x20B4u+(uint)i*4,sample.SegmentTexture(i)));
            yield return entry with { NativeClass=family.Name,Preset=mode,IsEventPreset=true,OneTime=true,
                Status=SacredParticleDefinitionStatus.Decoded,Diagnostic=null,TerrorSprinter=d,TextureBindings=bindings.AsReadOnly(),
                Capacity=NativeParticleCapacityReader.Read(code,entry.FactoryAddress),
                DisplayName=mode==0 ? "Native large rune ring; fixed eight-second lifetime" : "Native small rune ring; explicit ten-second combat duration sample" };
        }
    }
}
