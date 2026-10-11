using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeStonedEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x785EE0) yield break;
        var family = new NativeParticleFamily("Stoned", entry.FactoryAddress, 0x785EE0, 0x785F8F,
            0x786120, 0x20C4, 0, 0, 0, 0x786030);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        var modelType = code.At(0x785F23).Immediate32;
        if (modelType != code.At(0x7860B7).Immediate32 || code.At(0x786216).GetImmediate(1) != 0x14)
            throw new NotSupportedException("Unmapped Stoned model/material or saved block.");
        var definition = new SacredStonedFxDefinition(modelType, image.String(code.At(0x785F4D).Immediate32, 128),
            image.String(code.At(0x785F5E).Immediate32, 128))
        {
            DefaultDuration = BitConverter.UInt32BitsToSingle((uint)code.At(0x78618A).GetImmediate(1)),
            RandomScale = Operand(0x78614B), FullTurn = Operand(0x786151),
            StartNotification = (uint)code.At(0x7861C1).GetImmediate(1),
            OwnerTextureMask = (uint)code.At(0x7861AD).GetImmediate(1),
            OwnerTextureOperation = (uint)code.At(0x7861B7).GetImmediate(1)
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, Stoned = definition,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            TextureBindings = NativeTextureReader.ReadBindings(image, code, family),
            DisplayName = "Native randomized Stoned model; default three-second duration"
        };
    }
}
