using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Three serialized IEEE 754 words; interpretation remains separate from world X/Y.</summary>
public readonly record struct GoldPhysicalVector(float X, float Y, float Z)
{
    internal static GoldPhysicalVector Read(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
}
