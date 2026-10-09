using Iced.Intel;
using Sacred.Particles.Particles;

namespace Sacred.Particles.Reader.Decoding;

internal sealed record NativeParticleFamily(string Name, uint FactoryBranch, uint Constructor,
    uint ConstructorEnd, uint Initializer, int ObjectSize, int MotionOffset, int EmissionOffset,
    int ParameterSlotCount, uint DrawAddress)
{
    public (uint Start, uint End)? ActorLookup { get; init; }
    /// <summary>Evaluate the native radius-dependent branch at unit radius; runtime supplies Items.pak radius.</summary>
    public bool UnitActorRadius { get; init; }
    /// <summary>Native allocated slots exposed to initializers that derive their burst count from the vector.</summary>
    public int InitialVectorCount { get; init; }
    public int SelectorOffset { get; init; } = 0x38;
    public uint? EventColor { get; init; }
    public IReadOnlyDictionary<int, uint>? EventArguments { get; init; }
    public bool SampleDefaultEnvironment { get; init; }
    public uint? SampleRandomValue { get; init; }
    public int? ColorOffset { get; init; }
    public int ColorSlotStride { get; init; }
    public uint? InitialFlags { get; init; }
    public bool EmitBeforeMovement { get; init; } = true;
    public uint? InitializerEnd { get; init; }
    public Register DrawRegister { get; init; } = Register.EBX;
    // Native class implementations, identified by code address rather than fixture/type IDs.
    private static readonly NativeParticleFamily[] Families =
    [
        new("cParticleSystem_smoke", 0x5A0CCF, 0x76E200, 0x76E2D0, 0x76E980,
            SacredSmokeParticleSystemLayout.SerializedSize, 0x2CAC, 0x2D0C, 3, 0x76E60C) { EmitBeforeMovement = false },
        new("cParticleSystem_dwarfmagic", 0x5A17DF, 0x78A2D0, 0x78A343, 0x78A5D0,
            SacredDwarfMagicParticleSystemLayout.SerializedSize, 0x24A8, 0x24C8, 1, 0x78A3B0),
        new("cParticleSystem_magicprison", 0x5A1DA3, 0x79C960, 0x79C9D1, 0x79CC80,
            0x2530, 0x24A8, 0x24C8, 1, 0x79CA4C)
        {
            // Parameters and all 256 colors are complete here; the following call starts audio.
            InitializerEnd = 0x79CD8D,
            EmitBeforeMovement = false,
            DrawRegister = Register.EBP
        }
    ];

    public static NativeParticleFamily? Find(uint factoryBranch) =>
        Families.FirstOrDefault(f => f.FactoryBranch == factoryBranch);
}
