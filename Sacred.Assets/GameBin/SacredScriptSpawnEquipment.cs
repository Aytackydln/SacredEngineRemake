using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>One file-authored opcode 115 rule. Building the rule does not select,
/// allocate, equip or publish objects. Pool indices and chances retain their signed values.</summary>
public sealed class SacredScriptSpawnEquipment
{
    private readonly byte[] _data;
    private SacredScriptSpawnEquipment(byte[] data) => _data = data;
    public int TypeCount => Read(SacredScriptSpawnEquipmentLayout.TypeCountOffset);
    public int Selector => Read(SacredScriptSpawnEquipmentLayout.SelectorOffset);
    public uint ScriptResourceId => unchecked((uint)Read(SacredScriptSpawnEquipmentLayout.ResolvedObjectOffset));
    public int SpellCount => Read(SacredScriptSpawnEquipmentLayout.AdditionalCountOffset);
    public uint GetSpell(int index) => (uint)index < SpellCount
        ? unchecked((uint)Read(SacredScriptSpawnEquipmentLayout.AdditionalValuesOffset + index * 4)) : throw new ArgumentOutOfRangeException(nameof(index));
    public int GetType(int index) => (uint)index < TypeCount ? Read(index * 4) : throw new ArgumentOutOfRangeException(nameof(index));
    public bool Contains(uint type)
    {
        for (var index = 0; index < TypeCount; index++)
            if (unchecked((uint)GetType(index)) == type) return true;
        return false;
    }
    public (int Index, int Chance) GetPool(int index)
    {
        if ((uint)index >= 3) throw new ArgumentOutOfRangeException(nameof(index));
        var offset = SacredScriptSpawnEquipmentLayout.FirstPoolOffset + index * 8;
        return (Read(offset), Read(offset + SacredScriptSpawnEquipmentLayout.PoolChanceOffset));
    }
    public byte[] ToPayload() => _data.ToArray();
    private int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset));

    public static SacredScriptSpawnEquipment Read(SacredScriptCommand command, Func<string, uint?>? resolveType = null)
    {
        if (command.Opcode != SacredScriptSpawnEquipmentLayout.Opcode)
            throw new ArgumentException("Expected spawn-equipment opcode 115.", nameof(command));
        var data = new byte[SacredScriptSpawnEquipmentLayout.Size];
        Write(SacredScriptSpawnEquipmentLayout.FirstPoolOffset, -1);
        Write(SacredScriptSpawnEquipmentLayout.ThirdPoolOffset, -1);
        var count = 0;
        var spellCount = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.TypeId:
                    if (count < SacredScriptSpawnEquipmentLayout.TypeCapacity) Write(count++ * 4, operand.Integer!.Value);
                    break;
                case SacredScriptArgumentKind.SpawnEquipmentFirstPool: Pair(SacredScriptSpawnEquipmentLayout.FirstPoolOffset); break;
                case SacredScriptArgumentKind.SpawnEquipmentSecondPool: Pair(SacredScriptSpawnEquipmentLayout.SecondPoolOffset); break;
                case SacredScriptArgumentKind.SpawnEquipmentThirdPool: Pair(SacredScriptSpawnEquipmentLayout.ThirdPoolOffset); break;
                case SacredScriptArgumentKind.SpawnEquipmentSelector: Write(SacredScriptSpawnEquipmentLayout.SelectorOffset, operand.Integer!.Value); break;
                case SacredScriptArgumentKind.ScriptResourceId:
                    Write(SacredScriptSpawnEquipmentLayout.ResolvedObjectOffset, operand.Integer!.Value);
                    break;
                case SacredScriptArgumentKind.CreatureSpellSelector:
                    // Native operand decoding precedes the ten-entry append limit.
                    var spell = SacredScriptCreatureSpellSelector.Resolve(operand.Text!, resolveType);
                    if (spellCount < SacredScriptSpawnEquipmentLayout.AdditionalCapacity)
                        Write(SacredScriptSpawnEquipmentLayout.AdditionalValuesOffset + spellCount++ * 4, unchecked((int)spell));
                    break;
                // These operands have no case in 0x4AB940. Their bounded decoding still runs.
                case SacredScriptArgumentKind.Integer:
                case SacredScriptArgumentKind.Facing:
                case SacredScriptArgumentKind.CreatureAggressive:
                case SacredScriptArgumentKind.CreatureNonAggressive:
                    break;
                default:
                    throw new InvalidDataException($"Spawn-equipment operand 0x{(byte)operand.Kind:X2} requires its native resolution/effect at +0x{operand.Offset:X}.");
            }
            void Pair(int offset)
            {
                Write(offset, operand.Integer!.Value);
                Write(offset + SacredScriptSpawnEquipmentLayout.PoolChanceOffset, operand.SecondInteger!.Value);
            }
        }
        Write(SacredScriptSpawnEquipmentLayout.TypeCountOffset, count);
        Write(SacredScriptSpawnEquipmentLayout.AdditionalCountOffset, spellCount);
        return new(data);
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
    }
}
