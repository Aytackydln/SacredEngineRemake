using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeFxRingReader
{
    public static SacredFxRingDefinition Read(SacredExecutableImage image, NativeCode code,
        SacredParticleQuality quality, CancellationToken token, bool segmentTextures)
    {
        var memory=new PresetMemory(image,0x140,quality); var machine=new X86Machine(code,memory);
        var stack=memory.BaseAddress+0x1F000;
        memory.Write(stack+0x1C,4,0);
        machine.Registers.Set(Register.ESP,stack); machine.Registers.Set(Register.ECX,memory.BaseAddress);
        machine.Run(segmentTextures ? 0x7638E0u : 0x763530u,segmentTextures ? 0x7639DBu : 0x76362Bu,token);
        var vertices=MemoryMarshal.Cast<byte,SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0xC0,128)).ToArray();
        var angleAddress=(uint)code.At(segmentTextures ? 0x763BE3u : 0x763850u).MemoryDisplacement64;
        var initialAddress=(uint)code.At(segmentTextures ? 0x763B44u : 0x7637BCu).MemoryDisplacement64;
        return new(Array.AsReadOnly(vertices),image.Single(angleAddress),BinaryPrimitives.ReadDoubleLittleEndian(image.Slice(initialAddress,8)));
    }
}
