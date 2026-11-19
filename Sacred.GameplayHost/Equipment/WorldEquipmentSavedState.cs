using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Original item file-read state, before C2 reference replacement and
/// equipment attachment. Retains all physical/list/weapon data independently
/// of packet conversion. Values include the file reader's price side effects.</summary>
public sealed class WorldEquipmentSavedState
{
    private readonly byte[] _values;
    internal WorldEquipmentSavedState(SacredSavedItem source, GoldObjectBaseState fileBase, uint physicalFlags, byte[] values)
        => (Source, FileBase, PhysicalFlags, _values) = (source, fileBase, physicalFlags, values.ToArray());
    public SacredSavedItem Source { get; }
    public GoldObjectBaseState FileBase { get; }
    public uint PhysicalFlags { get; }
    public byte[] CopyValues() => _values.ToArray();
    public short QuestNumber => BinaryPrimitives.ReadInt16LittleEndian(Source.PhysicalData[SacredSavedPhysicalLayout.QuestNumberOffset..]);
    public ushort QuestEffect => BinaryPrimitives.ReadUInt16LittleEndian(Source.PhysicalData[SacredSavedPhysicalLayout.QuestEffectOffset..]);
}
