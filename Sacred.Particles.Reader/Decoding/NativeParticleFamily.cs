using Sacred.Particles.Particles;

namespace Sacred.Particles.Reader.Decoding;

internal sealed record NativeParticleFamily(string Name, uint FactoryBranch, uint Constructor,
    uint ConstructorEnd, uint Initializer, int ObjectSize, int MotionOffset, int EmissionOffset,
    int ParameterSlotCount, uint DrawAddress)
{
    // Native class implementations, identified by code address rather than fixture/type IDs.
    private static readonly NativeParticleFamily[] Families =
    [
        new("cParticleSystem_smoke", 0x5A0CCF, 0x76E200, 0x76E2D0, 0x76E980,
            SacredSmokeParticleSystemLayout.SerializedSize, 0x2CAC, 0x2D0C, 3, 0x76E60C),
        new("cParticleSystem_dwarfmagic", 0x5A17DF, 0x78A2D0, 0x78A343, 0x78A5D0,
            SacredDwarfMagicParticleSystemLayout.SerializedSize, 0x24A8, 0x24C8, 1, 0x78A3B0)
    ];

    public static NativeParticleFamily? Find(uint factoryBranch) =>
        Families.FirstOrDefault(f => f.FactoryBranch == factoryBranch);
}
