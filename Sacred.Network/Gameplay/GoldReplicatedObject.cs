namespace Sacred.Network.Gameplay;

/// <summary>Latest authoritative layers and route of one object. Presentation playback never modifies these anchors.</summary>
public sealed record GoldReplicatedObject(GoldObjectBaseState BaseState, GoldCreatureState? Creature,
    GoldItemState? Item, GoldMovementEvent? Movement = null)
{
    /// <summary>Identifies one lifetime of a runtime reference; movement and activation preserve it.</summary>
    public long InstanceVersion { get; init; }
    /// <summary>Latest concrete item values generation; movement preserves equipped appearance.</summary>
    public long ItemValuesVersion { get; init; }
    /// <summary>Physical state for every 3D instance, including doors and world props.</summary>
    public GoldPhysicalState? Physical { get; init; }
    public GoldObjectFxState? Fx { get; init; }
    /// <summary>Container extension selected by the original Items.pak category, never by payload length.</summary>
    public GoldContainerState? Container { get; init; }
    /// <summary>Locally dispatched world-object activation generation.</summary>
    public long ActivationVersion { get; init; }
    /// <summary>Trigger state after the local model activation; later received changes override its pose.</summary>
    public ushort? ActivationTriggerState { get; init; }
    /// <summary>Latest received values block, from a full object stream or 0x11B; no derived calculations.</summary>
    public GoldCreatureValuesState? Values { get; init; }
    /// <summary>Projected-world heading bits from position/end packets, or derived from a full physical direction vector.
    /// The base block's cached +0x28 word remains separately preserved.</summary>
    public uint? FacingBits { get; init; }
    /// <summary>Native +0x594/+0x5A8 slot. Retain until the native transition completion is implemented.</summary>
    public GoldDeferredPosition? DeferredPosition { get; init; }
    /// <summary>Latest creature +0x570 link: horse on a rider, rider on a horse. Snapshot tail bytes remain intact.</summary>
    public uint? MountPartnerReference { get; init; }
    /// <summary>Source of the latest reported anchor; presentation uses relocation/full state as a discontinuity.</summary>
    public ushort? PositionOpcode { get; init; }
    /// <summary>Advances for each received position, including identical-coordinate relocations.</summary>
    public long PositionVersion { get; init; }
    /// <summary>Route/stop/cancellation generation, including cancellation without a position.</summary>
    public long MovementVersion { get; init; }
    /// <summary>Movement generation supplied by the latest creature snapshot. A later
    /// stop/cancellation invalidates its embedded route, including for newly loaded consumers.</summary>
    public long SnapshotMovementVersion { get; init; }
    /// <summary>Latest native 0x166/0x167 removal form. A full replacement starts a new active instance.</summary>
    public ushort? RemovalOpcode { get; init; }
    /// <summary>Latest accepted SetupFly command; playback keeps its clock outside authoritative state.</summary>
    public GoldObjectFlightEvent? Flight { get; init; }
    public long FlightVersion { get; init; }
    /// <summary>Client TP[2] after received damage and absolute HP corrections, also available without a values block.</summary>
    public int? CurrentHitPoints { get; init; }
    /// <summary>Last absolute HP from a values/health packet, before locally received damage.</summary>
    public int? ReportedHitPoints { get; init; }
    public GoldCreatureAttackEvent? Attack { get; init; }
    public GoldCreatureSpecialMoveEvent? SpecialMove { get; init; }
    public GoldCreatureCastEvent? Cast { get; init; }
    /// <summary>Retained independently of the bounded combat history; replaced by each accepted sleep receipt.</summary>
    public GoldCreatureSleepEvent? Sleep { get; init; }
    public long AttackVersion { get; init; }
}
