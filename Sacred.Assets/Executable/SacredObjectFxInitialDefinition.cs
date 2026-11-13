using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Shared cObjectFX constructor operands and per-resource dispatch.
/// Particle-family constructors and Start state remain separate definitions.</summary>
public sealed record SacredObjectFxInitialDefinition(uint Resource, uint EventCode, uint NodePeriod,
    uint BaseFlags, uint RechargeBits, uint WordE0Bits)
{
    public static SacredObjectFxInitialDefinition Read(byte[] image, uint resource)
    {
        var code = new SacredNativeTypeSwitch(image);
        ReadOnlySpan<byte> anchor = [0x66, 0x89, 0x5E, 0x48, 0x66, 0x89, 0x5E, 0x6E, 0x66, 0x89, 0x5E, 0x70];
        var at = image.AsSpan().IndexOf(anchor);
        if (at < 0 || image.AsSpan(at + 1).IndexOf(anchor) >= 0)
            throw new InvalidDataException("Unique cObjectFX constructor unavailable.");
        var constructor = at - 0x67;
        if (!image.AsSpan(constructor + 0x1E).StartsWith<byte>([0x55, 0x6A]) ||
            !image.AsSpan(constructor + 0x116).StartsWith<byte>([0xC7, 0x86, 0xDC, 0, 0, 0]) ||
            !image.AsSpan(constructor + 0x120).StartsWith<byte>([0xC7, 0x86, 0xE0, 0, 0, 0]))
            throw new InvalidDataException("Unsupported cObjectFX initialization operands.");
        return new(resource, SacredFixedEffectDefinitions.Event(image, code, resource) ??
            throw new InvalidDataException("Resource has no compared CreateObj FX event."), image[constructor + 0x20],
            SacredFixedEffectDefinitions.Flags(image, code, resource),
            Word(constructor + 0x11C), Word(constructor + 0x126));
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset));
    }
}
