using System.Buffers.Binary;

namespace Sacred.Core.Runtime;

/// <summary>On-foot, nonlocal actor value initialization at the start of Gold
/// 0x5796A0, before equipment traversal. The caller supplies the original slow
/// speed predicate and 0x549000 difficulty predicate. This is not CalcResults.
/// The nullable return preserves TP[4] when the native prefix does not write it.</summary>
public static class SacredCreatureResultInitialization
{
    public static uint? Apply(Span<byte> values, bool slowDefaultSpeeds, uint objectReference,
        uint difficulty, bool difficultyBoostApplies, byte? templateEndurance)
    {
        if (values.Length != SacredActorValuesLayout.Size) throw new ArgumentException("Expected 238-byte actor values.", nameof(values));
        var level = BinaryPrimitives.ReadUInt16LittleEndian(values[78..]);
        for (var i = 0; i < 6; i++)
        {
            var basis = BinaryPrimitives.ReadUInt16LittleEndian(values[(66 + 2 * i)..]);
            var grown = unchecked((ushort)(basis + unchecked(basis * ((int)level - 1)) / 10 + values[20 + i]));
            BinaryPrimitives.WriteUInt16LittleEndian(values[(8 + 2 * i)..], grown);
        }
        // MaximumHealth is deliberately retained for the later health stage.
        values.Slice(82, 8).Clear();
        values[94..].Clear();
        foreach (var offset in new[] { 190, 194, 198, 202, 222, 226, 230, 234 })
            BinaryPrimitives.WriteSingleLittleEndian(values[offset..], 1);
        values.Slice(44, 8).Clear();
        BinaryPrimitives.WriteInt16LittleEndian(values[52..], slowDefaultSpeeds ? (short)100 : (short)140);
        BinaryPrimitives.WriteInt16LittleEndian(values[54..], slowDefaultSpeeds ? (short)100 : (short)120);
        BinaryPrimitives.WriteInt16LittleEndian(values[56..], slowDefaultSpeeds ? (short)120 : (short)130);
        if (objectReference == 0 || !templateEndurance.HasValue) return null;
        if (difficulty is 3 or 4 && difficultyBoostApplies)
        {
            var basis = difficulty == 4 ? templateEndurance.Value * 2 : templateEndurance.Value * 3 / 2;
            var grown = unchecked((ushort)(basis + basis * ((int)level - 1) / 10));
            // Native stores into runtime Basis[3], not a template copy or an
            // allocation byte. Repeated recalculation starts from that state.
            BinaryPrimitives.WriteUInt16LittleEndian(values[72..], (ushort)basis);
            BinaryPrimitives.WriteUInt16LittleEndian(values[14..], grown);
            BinaryPrimitives.WriteInt16LittleEndian(values[52..], difficulty == 4 ? (short)200 : (short)170);
            BinaryPrimitives.WriteInt16LittleEndian(values[56..], difficulty == 4 ? (short)190 : (short)160);
            return difficulty == 4 ? grown : (uint)grown * 3 / 2;
        }
        return (uint)BinaryPrimitives.ReadUInt16LittleEndian(values[14..]) * 2;
    }
}
