using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

public readonly record struct WorldCreatureRetainedEquipment(uint Reference, uint Value, byte Flags);

/// <summary>Authoritative nineteen-slot construction state. Assignment has
/// native object/attachment/result effects beyond the replica's slot reducer.</summary>
public sealed class WorldCreatureEquipmentState
{
    private readonly uint[] _references;
    private readonly List<WorldCreatureRetainedEquipment> _retained = [];

    public WorldCreatureEquipmentState(GoldObjectBaseState state, bool horse, ReadOnlySpan<uint> references)
    {
        if (references.Length != 19) throw new ArgumentException("Expected nineteen native slots.", nameof(references));
        Base = state;
        Horse = horse;
        _references = references.ToArray();
    }

    public GoldObjectBaseState Base { get; set; }
    public bool Horse { get; }
    public uint Behavior { get; set; }
    public ReadOnlySpan<uint> References => _references;
    public IReadOnlyList<WorldCreatureRetainedEquipment> Retained => _retained;
    public uint this[ushort slot] { get => _references[slot]; set => _references[slot] = value; }
    internal void Retain(uint reference) => _retained.Add(new(reference, 100, 0));
}
