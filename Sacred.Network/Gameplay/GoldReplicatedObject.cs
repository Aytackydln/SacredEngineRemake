namespace Sacred.Network.Gameplay;

/// <summary>Latest authoritative layers and route of one object. Presentation playback never modifies these anchors.</summary>
public sealed record GoldReplicatedObject(GoldObjectBaseState BaseState, GoldCreatureState? Creature,
    GoldItemState? Item, GoldMovementEvent? Movement = null)
{
    /// <summary>Identifies one lifetime of a runtime reference; movement and activation preserve it.</summary>
    public long InstanceVersion { get; init; }
    /// <summary>Physical state for every 3D instance, including doors and world props.</summary>
    public GoldPhysicalState? Physical { get; init; }
    /// <summary>Locally dispatched world-object activation generation.</summary>
    public long ActivationVersion { get; init; }
    /// <summary>Trigger state after the local model activation; later received changes override its pose.</summary>
    public ushort? ActivationTriggerState { get; init; }
    /// <summary>Latest received values block, from a full object stream or 0x11B; no derived calculations.</summary>
    public GoldCreatureValuesState? Values { get; init; }
    /// <summary>Projected-world heading bits from position/end packets, or derived from a full physical direction vector.
    /// The base block's cached +0x28 word remains separately preserved.</summary>
    public uint? FacingBits { get; init; }
    /// <summary>Latest creature +0x570 link: horse on a rider, rider on a horse. Snapshot tail bytes remain intact.</summary>
    public uint? MountPartnerReference { get; init; }
    /// <summary>Source of the latest reported anchor; presentation uses relocation/full state as a discontinuity.</summary>
    public ushort? PositionOpcode { get; init; }
    /// <summary>Advances for each received position, including identical-coordinate relocations.</summary>
    public long PositionVersion { get; init; }
    /// <summary>Route/stop/cancellation generation, including cancellation without a position.</summary>
    public long MovementVersion { get; init; }
    /// <summary>Latest native 0x166/0x167 removal form. A full replacement starts a new active instance.</summary>
    public ushort? RemovalOpcode { get; init; }
}
