using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Finite point bursts in native families previously absent from the runtime catalogue.</summary>
internal static class NativePointBurstReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var capacity = entry.FactoryAddress is 0x5A27C6 or 0x5A2F8C
            ? NativeParticleCapacityReader.Read(code, entry.FactoryAddress) : 0;
        NativeParticleFamily? family = entry.FactoryAddress switch
        {
            0x5A27C6 => new("cParticleSystem_sparks", entry.FactoryAddress,
                0x7AEFC0, 0x7AF031, 0x7AF2E0,
                0x20A0 + SacredSparksParticleStateLayout.SerializedSize + 4, 0x24AC, 0x24CC, 1, 0x7AF0CC)
                { InitialVectorCount = capacity },
            0x5A2F8C => new("cParticleSystem_dustcloud", entry.FactoryAddress,
                0x7C29A0, 0x7C2A11, 0x7C2B10,
                0x20A0 + SacredDustCloudParticleStateLayout.SerializedSize + 4, 0x24A8, 0x24C8, 1, 0x7C2AAC)
                { EmitBeforeMovement = false },
            _ => null
        };
        if (family is null) yield break;
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var parameters = NativePresetReader.Read(image, code, family, 0, quality, token);
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            BurstOnFirstUpdate = true, EmitBeforeMovement = family.EmitBeforeMovement,
            Capacity = capacity, Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
            TextureBindings = bindings, ParameterSets = parameters,
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single())
        };
    }
}
