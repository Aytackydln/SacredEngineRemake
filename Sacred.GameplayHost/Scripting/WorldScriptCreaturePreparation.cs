using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Private NPC construction and ordered publication buffers. Completion
/// of one object does not grant campaign upload, host finish or readiness.</summary>
public sealed class WorldScriptCreaturePreparation
{
    private readonly List<WorldEquipmentInitialItem> _equipment = [];
    private readonly List<GoldMessage> _publications = [];
    public WorldScriptCreaturePreparation(WorldScriptCreatureBlueprint blueprint, ushort facingDegrees)
    {
        Blueprint = blueprint;
        FacingDegrees = facingDegrees;
        Equipment = _equipment.AsReadOnly();
        Publications = _publications.AsReadOnly();
    }
    public WorldScriptCreatureBlueprint Blueprint { get; }
    public ushort FacingDegrees { get; }
    public IReadOnlyList<WorldEquipmentInitialItem> Equipment { get; }
    public IReadOnlyList<GoldMessage> Publications { get; }
    public WorldCreatureInitialActor? Actor { get; internal set; }
    public WorldEquipmentInitialItem? PendingEquipment { get; internal set; }
    public GoldCreatureState? CreatureState { get; internal set; }
    /// <summary>Shared RNG checkpoint after this NPC's complete preparation.</summary>
    public uint? RandomStateAfter { get; internal set; }
    public string Stage { get; internal set; } = "decoded";
    public bool PacketReady { get; private set; }
    internal void Complete(IReadOnlyList<GoldMessage> publications)
    {
        if (PacketReady || CreatureState is null || publications.Count == 0) throw new InvalidOperationException("Initial NPC publication is incomplete or already prepared.");
        _publications.AddRange(publications); PacketReady = true; Stage = "prepared";
    }
    internal void AddEquipment(WorldEquipmentInitialItem item) { _equipment.Add(item); PendingEquipment = item; }
}
