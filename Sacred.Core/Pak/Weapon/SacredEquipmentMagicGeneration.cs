using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D2E40, sockets and magic allocation for an equipment record.</summary>
public static class SacredEquipmentMagicGeneration
{
    public static void Apply(Span<byte> values, SacredItemCategory category, byte usage, uint weaponType,
        uint quality, uint difficulty, bool singleBonus, SacredEquipmentGenerationTuning tuning,
        SacredRandomSequence random, Func<uint, byte> combatArtClass)
    {
        var sockets = tuning.Sockets;
        var requested = unchecked((ushort)sockets.Budget(quality));
        if (requested == 0)
        {
            SacredEquipmentSocketGeneration.ApplyZeroQuality(values, category, sockets.SocketMaximum(weaponType), quality, difficulty, sockets, random);
            return;
        }
        var ranges = (tuning.Magic ?? throw new InvalidOperationException("Original magic pools unavailable.")).Pool(weaponType);
        var jewelry = category is SacredItemCategory.Ring or SacredItemCategory.Amulet;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(values[4..]);
        var cost = (float)sockets.Budget((flags >> 16) & 15);
        var multiplier = 1f;
        var firstSocket = random.Next();
        var maximum = sockets.SocketMaximum(weaponType);
        if ((uint)(firstSocket % 1000) < sockets.FirstChance && !jewelry && maximum != 0)
        {
            var count = 1;
            while (count < maximum && (uint)(random.Next() % 1000) < sockets.AdditionalChance) count++;
            for (var i = 0; i < count; i++)
            {
                var kind = Math.Clamp((difficulty == 0 ? 0 : difficulty == 1 ? 1 : 2) + random.Next() % 251 / 100, 1, 3);
                values[12 + i] = (byte)kind;
                multiplier += sockets.Costs[kind - 1];
            }
        }
        cost *= multiplier;
        if (requested > cost && (flags & 0xF0000) != 0xF0000)
        {
            var remaining = (int)(requested - cost);
            if (ReadGroup(values, 0) == 0) { WriteGroup(values, 0, 820); WriteAmount(values, 0, 1); }
            var shares = Distribute(remaining, multiplier, jewelry || singleBonus, random);
            var firstFamily = SacredEquipmentMagicBonusSelection.Family((ushort)ReadGroup(values, 0));
            var kinds = new List<int>();
            if (singleBonus)
            {
                int kind;
                do { kind = random.Next() % 4; } while (kind == firstFamily);
                kinds.Add(kind);
            }
            else if (jewelry && random.Next() % 100 < 50) kinds.Add(8);
            else
            {
                for (var i = 0; i < shares.Count; i++)
                {
                    if (i == 0 && !jewelry && requested * 1000d / (sockets.Budget(14) << 1) > random.Next() % 1000)
                    { kinds.Add(8); continue; }
                    int kind;
                    do { kind = random.Next() % 8; } while (kind == firstFamily || kinds.Contains(kind));
                    kinds.Add(kind);
                }
            }
            kinds.Sort();
            var failed = 0;
            var defenseSlot = -1;
            for (var i = 0; i < shares.Count && requested > cost; i++)
            {
                var slot = Math.Max(1, shares[i].Slot - failed);
                if (kinds[i] == 1) defenseSlot = slot;
                if (!SacredEquipmentMagicBonusSelection.Apply(kinds[i], values, slot, jewelry, (uint)shares[i].Budget,
                    category, usage, ranges, tuning.Bonuses, random, combatArtClass)) { failed++; continue; }
                WriteAmount(values, slot, 0);
                var added = SacredEquipmentMagicBonusValue.Add(values, slot, shares[i].Budget, category, difficulty, tuning.Bonuses, tuning.Normalization);
                cost = (float)(added * (double)multiplier + .5 + cost);
            }
            remaining = (int)(requested - cost);
            if (remaining > 0)
            {
                if (defenseSlot >= 0)
                    cost += SacredEquipmentMagicBonusValue.Add(values, defenseSlot, remaining, category, difficulty, tuning.Bonuses, tuning.Normalization);
                else if (firstFamily == 1 && SacredEquipmentMagicBonusSelection.Family((ushort)ReadGroup(values, 1)) == 0)
                    cost += SacredEquipmentMagicBonusValue.Add(values, 0, (int)(remaining / multiplier), category, difficulty, tuning.Bonuses, tuning.Normalization);
                else
                {
                    var savedGroup = ReadGroup(values, 1);
                    var savedAmount = BinaryPrimitives.ReadInt16LittleEndian(values[108..]);
                    var savedType = BinaryPrimitives.ReadUInt16LittleEndian(values[60..]);
                    BinaryPrimitives.WriteUInt16LittleEndian(values[60..], 0);
                    if (SacredEquipmentMagicBonusSelection.Apply(firstFamily == 1 ? 0 : 1, values, 1, jewelry, (uint)remaining,
                        category, usage, ranges, tuning.Bonuses, random, combatArtClass))
                    {
                        values.Slice(82, 16).CopyTo(values[86..]);
                        values.Slice(110, 8).CopyTo(values[112..]);
                        values.Slice(62, 8).CopyTo(values[64..]);
                        WriteGroup(values, 2, savedGroup); WriteAmount(values, 2, savedAmount);
                        BinaryPrimitives.WriteUInt16LittleEndian(values[62..], savedType);
                        WriteAmount(values, 1, 0);
                        // Original replenishment applies to slot zero after the
                        // attack branch, and slot one after the defense branch.
                        cost += SacredEquipmentMagicBonusValue.Add(values, firstFamily == 1 ? 0 : 1, (int)(remaining / multiplier),
                            category, difficulty, tuning.Bonuses, tuning.Normalization);
                    }
                }
            }
        }
        BinaryPrimitives.WriteUInt32LittleEndian(values[4..], (BinaryPrimitives.ReadUInt32LittleEndian(values[4..]) & 0xFFF0FFFF) |
            (unchecked((uint)(int)Math.Min(cost, 14f)) << 16));
    }

    private static List<(int Slot, int Budget)> Distribute(int budget, float multiplier, bool single, SacredRandomSequence random)
    {
        if (single) return [(1, budget & 0xFFFF)];
        var result = new List<(int Slot, int Budget)>();
        var remaining = (float)(budget & 0xFFFF);
        while (remaining > 0)
        {
            var assigned = false;
            for (var i = 0; i < result.Count; i++)
            {
                if (random.Next() % 100 >= 50 && result.Count != 4) continue;
                result[i] = (result[i].Slot, result[i].Budget + 1);
                remaining -= multiplier; assigned = true;
            }
            if (assigned) continue;
            result.Add((result.Count + 1, 1)); remaining -= multiplier;
        }
        return result;
    }
    private static uint ReadGroup(ReadOnlySpan<byte> values, int slot) => BinaryPrimitives.ReadUInt32LittleEndian(values[(74 + slot * 4)..]);
    private static void WriteGroup(Span<byte> values, int slot, uint group) => BinaryPrimitives.WriteUInt32LittleEndian(values[(74 + slot * 4)..], group);
    private static void WriteAmount(Span<byte> values, int slot, int amount) => BinaryPrimitives.WriteInt16LittleEndian(values[(106 + slot * 2)..], unchecked((short)amount));
}
